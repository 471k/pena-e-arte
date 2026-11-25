using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// C05: the AddBookingDraftClaimedAt backfill against real MySQL. A draft saved before the column existed was saved
/// by its owner while signed in (#460), so it must come out CLAIMED and still show on its owner's booking form.
/// </summary>
public sealed class BookingDraftClaimedAtMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261124184714_AddBookingDrafts";
    private const string TargetMigration = "20261125120630_AddBookingDraftClaimedAt";

    // Short prefix: Pomelo's migrations lock name must fit MySQL's 64-character limit.
    private readonly string _databaseName = $"bd_mig_{Guid.NewGuid():N}";

    private string ConnectionString =>
        $"Server=127.0.0.1;Port=3306;Database={_databaseName};User=root;Password=root;AllowPublicKeyRetrieval=true;SslMode=None;";

    private AppDbContext CreateContext(Guid? tenantId = null)
    {
        CurrentTenantService tenant = new();
        if (tenantId is Guid id) tenant.SetTenant(id);
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, DatabaseServerVersion.MySql)
            .Options;
        return new AppDbContext(options, tenant);
    }

    public async Task InitializeAsync()
    {
        await using AppDbContext ctx = CreateContext();
        await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    }

    public async Task DisposeAsync()
    {
        await using AppDbContext ctx = CreateContext();
        await ctx.Database.EnsureDeletedAsync();
    }

    private sealed record Caller(Guid UserId) : ICurrentUser
    {
        public string Role => "client";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
    }

    [Fact]
    public async Task ADraftSavedBeforeTheColumn_IsBackfilledAsClaimed_AndStillShowsForItsOwner()
    {
        Guid studioId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        DateTime created = new(2026, 11, 24, 19, 0, 0, DateTimeKind.Utc);

        await using (AppDbContext seed = CreateContext())
        {
            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO `BookingDrafts` (Id, UserId, PayloadJson, ExpiresAt, StudioId, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {5});",
                Guid.NewGuid().ToString(), ownerId.ToString(), """{"intake":{"tattooDescription":"saved under #460"}}""",
                DateTime.UtcNow.AddDays(5), studioId.ToString(), created);
        }

        await using (AppDbContext migrate = CreateContext())
        {
            await migrate.GetService<IMigrator>().MigrateAsync(TargetMigration);
        }

        await using AppDbContext read = CreateContext(studioId);
        DateTime? claimedAt = await read.BookingDrafts.Where(d => d.UserId == ownerId).Select(d => d.ClaimedAt).SingleAsync();
        claimedAt.Should().Be(created);

        BookingDraftResponse? mine = await new GetMyBookingDraftHandler(read, new Caller(ownerId), TimeProvider.System)
            .Handle(new GetMyBookingDraftQuery(), default);
        mine.Should().NotBeNull("a draft shipped before C05 must not vanish from its owner's screen");
        mine!.Payload.Should().Contain("saved under #460");
    }
}
