using FluentAssertions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class ExtendTrialHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private ExtendTrialHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_TrialingSubscription_ExtendsTrialExpiresAt()
    {
        Guid studioId     = Guid.NewGuid();
        DateTime original = DateTime.UtcNow.AddDays(3);
        await SeedStudio(studioId, SubscriptionStatus.Trialing, original);

        await CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(7)), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == studioId);
        stored.TrialExpiresAt.Should().BeCloseTo(original.AddDays(7), TimeSpan.FromSeconds(1));
        _db.Studios.Single(s => s.Id == studioId)
            .TrialExpiresAt.Should().BeCloseTo(original.AddDays(7), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ExpiredTrial_ExtendsFromNow()
    {
        Guid studioId     = Guid.NewGuid();
        DateTime original = DateTime.UtcNow.AddDays(-10);
        await SeedStudio(studioId, SubscriptionStatus.GracePeriod, original);

        await CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(7)), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == studioId);
        stored.TrialExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_GracePeriodSubscription_RevertsToTrialing()
    {
        Guid studioId = Guid.NewGuid();
        await SeedStudio(studioId, SubscriptionStatus.GracePeriod, DateTime.UtcNow.AddDays(-2));

        await CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(14)), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == studioId);
        stored.Status.Should().Be(SubscriptionStatus.Trialing);
        stored.GracePeriodEnd.Should().BeCloseTo(stored.TrialExpiresAt!.Value.AddDays(7), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_StudioWithoutSubscription_ExtendsStudioTrial()
    {
        Guid studioId     = Guid.NewGuid();
        DateTime original = DateTime.UtcNow.AddDays(5);
        _db.Studios.Add(new Studio
        {
            Id             = studioId,
            Name           = "No-Sub Studio",
            Slug           = "no-sub-studio",
            TrialExpiresAt = original,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(7)), default);

        _db.Studios.Single(s => s.Id == studioId)
            .TrialExpiresAt.Should().BeCloseTo(original.AddDays(7), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ActiveSubscription_ThrowsBusinessRuleViolation()
    {
        Guid studioId = Guid.NewGuid();
        await SeedStudio(studioId, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(30));

        Func<Task> act = () => CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(7)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new ExtendTrialCommand(Guid.NewGuid(), new ExtendTrialRequest(7)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedStudio(Guid studioId, SubscriptionStatus status, DateTime trialExpiry)
    {
        _db.Studios.Add(new Studio
        {
            Id             = studioId,
            Name           = "Test Studio",
            Slug           = $"test-{studioId:N}",
            TrialExpiresAt = trialExpiry,
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = studioId,
            Status           = status,
            TrialExpiresAt   = trialExpiry,
            GracePeriodEnd   = trialExpiry.AddDays(7),
            CurrentPeriodEnd = trialExpiry,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
