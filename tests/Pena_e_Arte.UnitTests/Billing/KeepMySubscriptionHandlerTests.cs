using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class KeepMySubscriptionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private KeepMySubscriptionHandler CreateSut() =>
        new(_db, _billing, NullLogger<KeepMySubscriptionHandler>.Instance);

    [Fact]
    public async Task Handle_ScheduledCancellation_ClearsFlag_AndUndoesInStripe()
    {
        const string stripeId = "sub_keep";
        await SeedSubscription(cancelAtPeriodEnd: true, stripeId);

        SubscriptionResponse result = await CreateSut().Handle(new KeepMySubscriptionCommand(_studioId), default);

        result.CancelAtPeriodEnd.Should().BeFalse();
        _db.Subscriptions.Single(s => s.StudioId == _studioId).CancelAtPeriodEnd.Should().BeFalse();
        await _billing.Received(1).UndoScheduledCancellationAsync(stripeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotScheduledToCancel_ThrowsBusinessRuleViolation()
    {
        await SeedSubscription(cancelAtPeriodEnd: false, "sub_notscheduled");

        Func<Task> act = () => CreateSut().Handle(new KeepMySubscriptionCommand(_studioId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoSubscription_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new KeepMySubscriptionCommand(_studioId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedSubscription(bool cancelAtPeriodEnd, string? stripeId)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = _studioId,
            Status = SubscriptionStatus.Active,
            BillingInterval = BillingInterval.Monthly,
            StripeSubscriptionId = stripeId,
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
