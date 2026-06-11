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
        await SeedSubscription(studioId, SubscriptionStatus.Trialing, original);

        await CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(7)), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == studioId);
        stored.TrialExpiresAt.Should().BeCloseTo(original.AddDays(7), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ActiveSubscription_ThrowsBusinessRuleViolation()
    {
        Guid studioId = Guid.NewGuid();
        await SeedSubscription(studioId, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(30));

        Func<Task> act = () => CreateSut().Handle(
            new ExtendTrialCommand(studioId, new ExtendTrialRequest(7)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoSubscription_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new ExtendTrialCommand(Guid.NewGuid(), new ExtendTrialRequest(7)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedSubscription(Guid studioId, SubscriptionStatus status, DateTime trialExpiry)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = studioId,
            Status           = status,
            TrialExpiresAt   = trialExpiry,
            CurrentPeriodEnd = trialExpiry,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
