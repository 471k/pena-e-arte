using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.Pok;
using StackExchange.Redis;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// PokAuthClient's credential lookup on real MySQL, under a job's conditions (no tenant: the reconciliation job
/// and every card payment resolve credentials this way). The lookup is a correlated subquery inside a
/// projection; a mistake in it does not fail safe, it fails every card payment with "not connected". So the
/// live path is the test that matters; the soft-deleted one is the latent case the predicate is for.
/// </summary>
[Collection("Database")]
public class PokCredentialResolutionIntegrationTests(DatabaseFixture fixture)
{
    private const string CachedToken = "cached-token";

    // Under the declared MySQL 8.4 (DatabaseServerVersion), EF writes this lookup's correlated subquery as a
    // LATERAL join. Below 8.0.14 it cannot translate it at all, which is how this query went untested while
    // the fixture declared 8.0.0. This context is built here, not via fixture.CreateDbContext, only because
    // it predates the shared constant. Moving self-built contexts onto the fixture is a separate backlog item.
    private AppDbContext JobContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, DatabaseServerVersion.MySql)
            .Options,
        new CurrentTenantService());

    private static PokAuthClient Client(AppDbContext db, ISecretsProvider secrets, Guid studioId)
    {
        // A cached bearer token, so no HTTP call is made: the test is about the database read.
        IDatabase cache = Substitute.For<IDatabase>();
        cache.StringGetAsync($"pok:token:{studioId}", Arg.Any<CommandFlags>()).Returns((RedisValue)CachedToken);
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(cache);

        return new PokAuthClient(db, secrets, Options.Create(new PokOptions { BaseUrl = "https://api-staging.pok.test" }),
            Substitute.For<IHttpClientFactory>(), redis, NullLogger<PokAuthClient>.Instance);
    }

    private async Task<Guid> SeedConnectedStudio(DateTime? credentialDeletedAt)
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext seed = fixture.CreateDbContext(studioId);
        seed.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "S",
            Slug = $"s-{studioId:N}",
            OwnerEmail = $"{studioId:N}@test.com",
            PokMerchantId = "merchant-1",
        });
        seed.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = $"studios/{studioId}/pok",
            DeletedAt = credentialDeletedAt,
        });
        await seed.SaveChangesAsync();
        return studioId;
    }

    [Fact]
    public async Task ALiveCredential_StillResolves_UnderJobConditions()
    {
        Guid studioId = await SeedConnectedStudio(credentialDeletedAt: null);
        ISecretsProvider secrets = Substitute.For<ISecretsProvider>();
        secrets.GetSecretAsync($"studios/{studioId}/pok:keyId", Arg.Any<CancellationToken>()).Returns("key-id");
        secrets.GetSecretAsync($"studios/{studioId}/pok:keySecret", Arg.Any<CancellationToken>()).Returns("key-secret");

        await using AppDbContext job = JobContext();
        (string merchantId, string token, HttpClient _) = await Client(job, secrets, studioId).PrepareRequestAsync(studioId, default);

        merchantId.Should().Be("merchant-1");
        token.Should().Be(CachedToken);
    }

    [Fact]
    public async Task ASoftDeletedCredential_IsNotUsed()
    {
        Guid studioId = await SeedConnectedStudio(credentialDeletedAt: DateTime.UtcNow);
        ISecretsProvider secrets = Substitute.For<ISecretsProvider>();

        await using AppDbContext job = JobContext();
        Func<Task> act = () => Client(job, secrets, studioId).PrepareRequestAsync(studioId, default);

        await act.Should().ThrowAsync<PaymentProviderNotConnectedException>();
        await secrets.DidNotReceiveWithAnyArgs().GetSecretAsync(default!, default);
    }
}
