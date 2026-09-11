using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ExternalApi.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExternalApi;

public class GenerateStudioApiKeyHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GenerateStudioApiKeyHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private GenerateStudioApiKeyHandler CreateSut() => new(_db, _tenant);

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

        Func<Task> act = () => CreateSut().Handle(new GenerateStudioApiKeyCommand(), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PlanAllowsApiAccess_ReturnsRawKeyAndPersistsOnlyItsHash()
    {
        await SeedStudioAsync(allowApiAccess: true);

        GenerateApiKeyResponse result = await CreateSut().Handle(new GenerateStudioApiKeyCommand(), default);

        result.ApiKey.Should().NotBeNullOrEmpty();
        result.KeyPrefix.Should().NotBeNullOrEmpty();
        result.ApiKey.Should().StartWith(result.KeyPrefix);

        StudioApiKey stored = _db.StudioApiKeys.Single();
        stored.KeyHash.Should().NotBe(result.ApiKey);
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingActiveKey_RevokesItBeforeCreatingNewOne()
    {
        await SeedStudioAsync(allowApiAccess: true);

        await CreateSut().Handle(new GenerateStudioApiKeyCommand(), default);
        await CreateSut().Handle(new GenerateStudioApiKeyCommand(), default);

        _db.StudioApiKeys.Should().HaveCount(2);
        _db.StudioApiKeys.Should().ContainSingle(k => k.RevokedAt == null);
    }
}
