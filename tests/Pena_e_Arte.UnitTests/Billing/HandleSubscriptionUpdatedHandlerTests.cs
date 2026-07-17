using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleSubscriptionUpdatedHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private HandleSubscriptionUpdatedHandler CreateSut() => new(_db);

    private static readonly DateTime _nextPeriodEnd = DateTime.UtcNow.AddMonths(1);

    [Theory]
    [InlineData("active",   SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("canceled", SubscriptionStatus.Cancelled)]
    public async Task Handle_KnownStripeStatus_MapsToExpectedStatus(
        string stripeStatus, SubscriptionStatus expected)
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, stripeStatus, _nextPeriodEnd, null), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_KnownStripeStatus_UpdatesCurrentPeriodEnd()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "active", _nextPeriodEnd, null), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .CurrentPeriodEnd.Should().BeCloseTo(_nextPeriodEnd, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_WithMatchingPriceId_UpdatesPlanId()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        Plan plan = new()
        {
            Name                 = "Pro",
            BillingInterval      = BillingInterval.Monthly,
            PriceMonthly         = 49m,
            StripePriceIdMonthly = "price_monthly123"
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "active", _nextPeriodEnd, "price_monthly123"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .PlanId.Should().Be(plan.Id);
    }

    [Fact]
    public async Task Handle_PriceMatchesPendingPlan_ClearsPendingPlanId()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";

        Plan plan = new()
        {
            Name                 = "Basic",
            BillingInterval      = BillingInterval.Monthly,
            PriceMonthly         = 29m,
            StripePriceIdMonthly = "price_basic_pending"
        };
        _db.Plans.Add(plan);
        _db.Subscriptions.Add(new Subscription
        {
            StudioId             = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status               = SubscriptionStatus.Active,
            PendingPlanId        = plan.Id,
            TrialExpiresAt       = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd     = DateTime.UtcNow.AddDays(1),
            GracePeriodEnd       = DateTime.UtcNow.AddDays(-13)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "active", _nextPeriodEnd, "price_basic_pending"), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId);
        stored.PlanId.Should().Be(plan.Id);
        stored.PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionsToActive_ClearsTrialExpiresAt()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "active", _nextPeriodEnd, null), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .TrialExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionsToTrialing_LeavesTrialExpiresAtUntouched()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.PastDue);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "trialing", _nextPeriodEnd, null), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .TrialExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownStripeStatus_DoesNotChangeStatus()
    {
        string stripeSubId = "sub_abc";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand(stripeSubId, "paused", _nextPeriodEnd, null), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_UnknownSubscription_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(
            new HandleSubscriptionUpdatedCommand("sub_unknown", "active", _nextPeriodEnd, null), default);

        await act.Should().NotThrowAsync();
    }

    private async Task SeedSubscription(string stripeSubId, SubscriptionStatus status)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId             = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status               = status,
            TrialExpiresAt       = DateTime.UtcNow.AddDays(14),
            CurrentPeriodEnd     = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd       = DateTime.UtcNow.AddDays(21)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
