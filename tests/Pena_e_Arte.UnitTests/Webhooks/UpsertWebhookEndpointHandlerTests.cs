using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Webhooks.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Webhooks;

public class UpsertWebhookEndpointHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ITokenEncryptor _encryptor = Substitute.For<ITokenEncryptor>();
    private readonly Guid _studioId = Guid.NewGuid();

    public UpsertWebhookEndpointHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _encryptor.Encrypt(Arg.Any<string>()).Returns(c => $"enc:{c.Arg<string>()}");
    }

    private UpsertWebhookEndpointHandler CreateSut() => new(_db, _tenant, _encryptor);

    private async Task SeedStudioAsync(bool allowApiAccess)
    {
        Plan plan = new() { Name = "Pro", AllowApiAccess = allowApiAccess };
        _db.Plans.Add(plan);
        Studio studio = new() { Id = _studioId, Name = "Ink Soul", Slug = "ink-soul", City = "Porto" };
        _db.Studios.Add(studio);
        _db.Subscriptions.Add(new Subscription { StudioId = _studioId, PlanId = plan.Id });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_PlanDoesNotAllowApiAccess_ThrowsBusinessRuleViolationException()
    {
        await SeedStudioAsync(allowApiAccess: false);

        Func<Task> act = () => CreateSut().Handle(
            new UpsertWebhookEndpointCommand("https://example.com/hook"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PlanAllows_CreatesEndpointAndReturnsSecretOnce()
    {
        await SeedStudioAsync(allowApiAccess: true);

        GenerateWebhookSecretResponse result = await CreateSut().Handle(
            new UpsertWebhookEndpointCommand("https://example.com/hook"), default);

        result.Url.Should().Be("https://example.com/hook");
        result.Secret.Should().NotBeNullOrEmpty();

        WebhookEndpoint stored = _db.WebhookEndpoints.Single();
        stored.Url.Should().Be("https://example.com/hook");
        stored.EncryptedSecret.Should().NotBe(result.Secret);
        stored.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CalledAgain_ReplacesExistingEndpointRatherThanAddingSecond()
    {
        await SeedStudioAsync(allowApiAccess: true);

        await CreateSut().Handle(new UpsertWebhookEndpointCommand("https://a.example.com/hook"), default);
        GenerateWebhookSecretResponse second = await CreateSut().Handle(
            new UpsertWebhookEndpointCommand("https://b.example.com/hook"), default);

        _db.WebhookEndpoints.Should().HaveCount(1);
        WebhookEndpoint stored = _db.WebhookEndpoints.Single();
        stored.Url.Should().Be("https://b.example.com/hook");
        stored.EncryptedSecret.Should().Be($"enc:{second.Secret}");
    }

    [Fact]
    public async Task Handle_ReplacingAfterFailures_ResetsFailureCountAndReactivates()
    {
        await SeedStudioAsync(allowApiAccess: true);
        await CreateSut().Handle(new UpsertWebhookEndpointCommand("https://a.example.com/hook"), default);

        WebhookEndpoint existing = _db.WebhookEndpoints.Single();
        existing.IsActive = false;
        existing.ConsecutiveFailureCount = 20;
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new UpsertWebhookEndpointCommand("https://b.example.com/hook"), default);

        WebhookEndpoint stored = _db.WebhookEndpoints.Single();
        stored.IsActive.Should().BeTrue();
        stored.ConsecutiveFailureCount.Should().Be(0);
    }
}
