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

    private static HandleSubscriptionUpdatedCommand Command(
        string stripeSubId, string status, string? priceId = null,
        long? unitAmount = null, string? currency = null, long? quantity = null,
        decimal? recurringDiscountPercent = null, bool cancelAtPeriodEnd = false) =>
        new(stripeSubId, status, _nextPeriodEnd, priceId,
            unitAmount, currency, quantity, recurringDiscountPercent, cancelAtPeriodEnd);

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("canceled", SubscriptionStatus.Cancelled)]
    public async Task Handle_KnownStripeStatus_MapsToExpectedStatus(
        string stripeStatus, SubscriptionStatus expected)
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(Command(stripeSubId, stripeStatus), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_KnownStripeStatus_UpdatesCurrentPeriodEnd()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(Command(stripeSubId, "active"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .CurrentPeriodEnd.Should().BeCloseTo(_nextPeriodEnd, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_WithMatchingPriceId_UpdatesPlanIdAndBillingInterval()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Monthly,
            Price = 49m,
            StripePriceId = "price_monthly123",
        });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "active", priceId: "price_monthly123"), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId);
        stored.PlanId.Should().Be(plan.Id);
        stored.BillingInterval.Should().Be(BillingInterval.Monthly);
    }

    [Fact]
    public async Task Handle_MatchingPriceId_YearlyInterval_SetsBillingIntervalYearly()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        Plan plan = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Yearly,
            Price = 790m,
            StripePriceId = "price_yearly123",
        });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "active", priceId: "price_yearly123"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .BillingInterval.Should().Be(BillingInterval.Yearly);
    }

    [Fact]
    public async Task Handle_PriceMatchesPendingPlanAndInterval_ClearsPendingFields()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";

        Plan plan = new() { Name = "Basic" };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Monthly,
            Price = 29m,
            StripePriceId = "price_basic_pending",
        });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = SubscriptionStatus.Active,
            PendingPlanId = plan.Id,
            PendingBillingInterval = BillingInterval.Monthly,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(1),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "active", priceId: "price_basic_pending"), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId);
        stored.PlanId.Should().Be(plan.Id);
        stored.PendingPlanId.Should().BeNull();
        stored.PendingBillingInterval.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionsToActive_ClearsTrialExpiresAt()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(Command(stripeSubId, "active"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .TrialExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionsToTrialing_LeavesTrialExpiresAtUntouched()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.PastDue);

        await CreateSut().Handle(Command(stripeSubId, "trialing"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .TrialExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownStripeStatus_DoesNotChangeStatus()
    {
        string stripeSubId = "sub_abc";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(Command(stripeSubId, "paused"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_TransitionsToPastDue_SetsPastDueSince()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(Command(stripeSubId, "past_due"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .PastDueSince.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_AlreadyPastDue_DoesNotResetPastDueSince()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.PastDue);
        DateTime originalPastDueSince = DateTime.UtcNow.AddDays(-3);
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId).PastDueSince = originalPastDueSince;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "past_due"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .PastDueSince.Should().BeCloseTo(originalPastDueSince, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_TransitionsFromPastDueToActive_ClearsPastDueSince()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.PastDue);
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId).PastDueSince = DateTime.UtcNow.AddDays(-3);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "active"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .PastDueSince.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionsFromPastDueToCancelled_ClearsPastDueSince()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.PastDue);
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId).PastDueSince = DateTime.UtcNow.AddDays(-3);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "canceled"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .PastDueSince.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnknownStripeStatus_LeavesPastDueSinceUnchanged()
    {
        string stripeSubId = "sub_paused_pd";
        await SeedSubscription(stripeSubId, SubscriptionStatus.PastDue);
        DateTime originalPastDueSince = DateTime.UtcNow.AddDays(-2);
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId).PastDueSince = originalPastDueSince;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "paused"), default);

        // Status stays PastDue (unknown Stripe status leaves it untouched) so PastDueSince
        // must also stay untouched, not get cleared by the != PastDue branch.
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .PastDueSince.Should().BeCloseTo(originalPastDueSince, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_UnknownSubscription_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(Command("sub_unknown", "active"), default);

        await act.Should().NotThrowAsync();
    }

    // --- Billed-amount snapshot (Batch 2b) ---

    [Fact]
    public async Task Handle_StatusOnlyEvent_NoPriceIdChange_StillRefreshesSnapshot()
    {
        // This is the regression Batch 2b exists to fix: a plain status-only webhook (no price
        // change) must still refresh the billed-amount snapshot from whatever Stripe reports
        // the subscription is billed at right now.
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(
            Command(stripeSubId, "active", unitAmount: 7900, currency: "eur", quantity: 1), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId);
        stored.BilledUnitAmount.Should().Be(79m);
        stored.BilledQuantity.Should().Be(1);
        stored.BilledCurrency.Should().Be("eur");
    }

    [Fact]
    public async Task Handle_NoUnitAmount_LeavesExistingSnapshotUntouched()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);
        Subscription seeded = _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId);
        seeded.BilledUnitAmount = 79m;
        seeded.BilledCurrency = "eur";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "active"), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .BilledUnitAmount.Should().Be(79m);
    }

    [Fact]
    public async Task Handle_RecurringDiscountPercentSet_IsPersisted()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);

        await CreateSut().Handle(
            Command(stripeSubId, "active", recurringDiscountPercent: 20m), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .RecurringDiscountPercent.Should().Be(20m);
    }

    [Fact]
    public async Task Handle_RecurringDiscountPercentNoLongerPresent_IsCleared()
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Active);
        Subscription seeded = _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId);
        seeded.RecurringDiscountPercent = 20m;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(Command(stripeSubId, "active", recurringDiscountPercent: null), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .RecurringDiscountPercent.Should().BeNull();
    }

    private async Task SeedSubscription(string stripeSubId, SubscriptionStatus status)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd = DateTime.UtcNow.AddDays(21)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    private async Task<string> SeedPaidSubscription(
        SubscriptionStatus status, decimal billedAmount = 59m, Guid? planId = null,
        Guid? pendingPlanId = null, BillingInterval? pendingInterval = null)
    {
        string stripeSubId = $"sub_{Guid.NewGuid():N}";
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = status,
            PlanId = planId,
            BillingInterval = BillingInterval.Monthly,
            PendingPlanId = pendingPlanId,
            PendingBillingInterval = pendingInterval,
            BilledUnitAmount = billedAmount,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return stripeSubId;
    }

    [Fact]
    public async Task Handle_ActiveToPastDue_WritesPastDueEventWithStripeEventId()
    {
        string subId = await SeedPaidSubscription(SubscriptionStatus.Active);

        await CreateSut().Handle(Command(subId, "past_due") with { StripeEventId = "evt_pd" }, default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.PastDue);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(59m);
        ledgerEvent.StripeEventId.Should().Be("evt_pd");
    }

    [Fact]
    public async Task Handle_PastDueToActive_WritesRecoveredEvent()
    {
        string subId = await SeedPaidSubscription(SubscriptionStatus.PastDue);

        await CreateSut().Handle(Command(subId, "active") with { StripeEventId = "evt_rec" }, default);

        _db.SubscriptionRevenueEvents.Single().Type.Should().Be(RevenueEventType.Recovered);
    }

    [Fact]
    public async Task Handle_SamePastDueWebhookDeliveredTwice_WritesOneEvent()
    {
        string subId = await SeedPaidSubscription(SubscriptionStatus.Active);
        HandleSubscriptionUpdatedCommand webhook = Command(subId, "past_due") with { StripeEventId = "evt_pd" };

        await CreateSut().Handle(webhook, default);
        _db.ChangeTracker.Clear();
        await CreateSut().Handle(webhook, default);

        _db.SubscriptionRevenueEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task Handle_ScheduledDowngradeLands_WritesContractionWithStripeEventId()
    {
        Plan premium = new() { Name = "Premium" };
        Plan growth = new() { Name = "Growth" };
        growth.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 59m, StripePriceId = "price_growth_m" });
        _db.Plans.AddRange(premium, growth);
        await _db.SaveChangesAsync();
        string subId = await SeedPaidSubscription(
            SubscriptionStatus.Active, billedAmount: 79m, planId: premium.Id,
            pendingPlanId: growth.Id, pendingInterval: BillingInterval.Monthly);

        await CreateSut().Handle(
            Command(subId, "active", priceId: "price_growth_m", unitAmount: 5900, currency: "eur", quantity: 1)
                with
            { StripeEventId = "evt_land" },
            default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Contraction);
        ledgerEvent.MrrBefore.Should().Be(79m);
        ledgerEvent.MrrAfter.Should().Be(59m);
        ledgerEvent.StripeEventId.Should().Be("evt_land");
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == subId).PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BareActiveAmountChangeWithNoPendingChange_WritesNoEvent()
    {
        // e.g. an out-of-band Stripe Dashboard price edit, or the echo of an immediate upgrade.
        string subId = await SeedPaidSubscription(SubscriptionStatus.Active, billedAmount: 59m);

        await CreateSut().Handle(
            Command(subId, "active", unitAmount: 7900, currency: "eur", quantity: 1), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ActiveToCanceledStatus_WritesChurnAndLaterDeletedEchoAddsNothing()
    {
        string subId = await SeedPaidSubscription(SubscriptionStatus.Active, billedAmount: 59m);

        await CreateSut().Handle(Command(subId, "canceled") with { StripeEventId = "evt_can" }, default);
        _db.ChangeTracker.Clear();
        await new HandleSubscriptionDeletedHandler(_db).Handle(new HandleSubscriptionDeletedCommand(subId, "evt_del"), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Churn);
        ledgerEvent.MrrBefore.Should().Be(59m);
        ledgerEvent.MrrAfter.Should().Be(0m);
    }
}
