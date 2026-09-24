using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class ChangePlanHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private static readonly DateTime _newPeriodEnd = DateTime.UtcNow.AddMonths(1);

    public ChangePlanHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _billing.ChangeSubscriptionPriceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_newPeriodEnd);
    }

    private ChangePlanHandler CreateSut() =>
        new(_db, _tenant, _billing, NullLogger<ChangePlanHandler>.Instance);

    // ── Upgrades ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UpgradeToHigherPricedPlan_SwitchesImmediately()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, "price_pro");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        result.PlanId.Should().Be(target.Id);
        result.BillingInterval.Should().Be("Monthly");
        result.PendingPlanId.Should().BeNull();
        result.CurrentPeriodEnd.Should().BeCloseTo(_newPeriodEnd, TimeSpan.FromSeconds(1));
        await _billing.Received(1).ChangeSubscriptionPriceAsync("sub_123", "price_pro", Arg.Any<CancellationToken>());
    }

    // ── Downgrades ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DowngradeToCheaperPlan_SchedulesChangeAtPeriodEnd()
    {
        Plan current = await SeedPlan("Pro", 79m, "price_pro");
        Plan target = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        result.PlanId.Should().Be(current.Id);          // still on the current plan
        result.PendingPlanId.Should().Be(target.Id);    // change is pending
        result.PendingBillingInterval.Should().Be("Monthly");
        await _billing.Received(1).ScheduleSubscriptionPriceChangeAsync(
            "sub_123", "price_pro", "price_basic", "month", Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ChangeSubscriptionPriceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Interval-only switch ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_SameTierDifferentInterval_ChangesOnlyBillingInterval()
    {
        Plan premium = new() { Name = "Premium" };
        premium.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m, StripePriceId = "price_premium_m" });
        premium.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m, StripePriceId = "price_premium_y" });
        _db.Plans.Add(premium);
        await _db.SaveChangesAsync();
        await SeedSubscription(premium.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        // Yearly's monthly-equivalent (790/12 ≈ 65.83) is cheaper than Monthly's 79 →
        // this is a downgrade path, scheduled at period end.
        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(premium.Id, "Yearly")), default);

        result.PlanId.Should().Be(premium.Id);
        result.BillingInterval.Should().Be("Monthly");   // unchanged until period end
        result.PendingPlanId.Should().Be(premium.Id);    // same tier
        result.PendingBillingInterval.Should().Be("Yearly");
        await _billing.Received(1).ScheduleSubscriptionPriceChangeAsync(
            "sub_123", "price_premium_m", "price_premium_y", "year", Arg.Any<CancellationToken>());
    }

    // ── D7: Yearly → Monthly always waits for period end ────────────────────

    [Fact]
    public async Task Handle_PremiumYearlyToPremiumMonthly_SchedulesInsteadOfUpgrading()
    {
        Plan premium = await SeedPlanWithBothIntervals("Premium", 79m, 790m, "price_premium_m", "price_premium_y");
        await SeedSubscription(premium.Id, BillingInterval.Yearly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(premium.Id, "Monthly")), default);

        result.PlanId.Should().Be(premium.Id);
        result.BillingInterval.Should().Be("Yearly"); // unchanged until period end
        result.PendingPlanId.Should().Be(premium.Id);
        result.PendingBillingInterval.Should().Be("Monthly");
        await _billing.Received(1).ScheduleSubscriptionPriceChangeAsync(
            "sub_123", "price_premium_y", "price_premium_m", "month", Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ChangeSubscriptionPriceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StarterYearlyToGrowthMonthly_SchedulesEvenThoughGrowthIsPricier()
    {
        // 59 > 24.17 (290/12) would normally read as an upgrade — D7 overrides that for any
        // Yearly → Monthly switch, whatever the tier.
        Plan starter = await SeedPlanWithBothIntervals("Starter", 29m, 290m, "price_starter_m", "price_starter_y");
        Plan growth = await SeedPlanWithBothIntervals("Growth", 59m, 590m, "price_growth_m", "price_growth_y");
        await SeedSubscription(starter.Id, BillingInterval.Yearly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(growth.Id, "Monthly")), default);

        result.PendingPlanId.Should().Be(growth.Id);
        result.PendingBillingInterval.Should().Be("Monthly");
        await _billing.Received(1).ScheduleSubscriptionPriceChangeAsync(
            "sub_123", "price_starter_y", "price_growth_m", "month", Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ChangeSubscriptionPriceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StarterYearlyToGrowthYearly_StillImmediateAndProrated()
    {
        Plan starter = await SeedPlanWithBothIntervals("Starter", 29m, 290m, "price_starter_m", "price_starter_y");
        Plan growth = await SeedPlanWithBothIntervals("Growth", 59m, 590m, "price_growth_m", "price_growth_y");
        await SeedSubscription(starter.Id, BillingInterval.Yearly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(growth.Id, "Yearly")), default);

        result.PlanId.Should().Be(growth.Id);
        result.PendingPlanId.Should().BeNull();
        await _billing.Received(1).ChangeSubscriptionPriceAsync(
            "sub_123", "price_growth_y", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GrowthMonthlyToPremiumMonthly_StillImmediate()
    {
        Plan growth = await SeedPlan("Growth", 59m, "price_growth_m");
        Plan premium = await SeedPlan("Premium", 79m, "price_premium_m");
        await SeedSubscription(growth.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(premium.Id, "Monthly")), default);

        result.PlanId.Should().Be(premium.Id);
        result.PendingPlanId.Should().BeNull();
        await _billing.Received(1).ChangeSubscriptionPriceAsync(
            "sub_123", "price_premium_m", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PremiumMonthlyToPremiumYearly_StillScheduledAtPeriodEnd()
    {
        Plan premium = await SeedPlanWithBothIntervals("Premium", 79m, 790m, "price_premium_m", "price_premium_y");
        await SeedSubscription(premium.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(premium.Id, "Yearly")), default);

        result.PendingPlanId.Should().Be(premium.Id);
        result.PendingBillingInterval.Should().Be("Yearly");
        await _billing.Received(1).ScheduleSubscriptionPriceChangeAsync(
            "sub_123", "price_premium_m", "price_premium_y", "year", Arg.Any<CancellationToken>());
    }

    // ── Guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SubscriptionNotActive_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, "price_pro");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Trialing, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*active subscription*");
    }

    [Fact]
    public async Task Handle_CashBilledSubscription_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, "price_pro");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, stripeSubId: null);

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*billed outside Stripe*");
    }

    [Fact]
    public async Task Handle_PendingChangeAlreadyScheduled_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Pro", 79m, "price_pro");
        Plan pending = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Studio", 129m, "price_studio");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123", pendingPlanId: pending.Id);

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already scheduled*");
    }

    [Fact]
    public async Task Handle_SamePlanAndInterval_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(current.Id, "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already on this plan*");
    }

    [Fact]
    public async Task Handle_PlanNotFound_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(Guid.NewGuid(), "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*not available at that billing interval*");
    }

    [Fact]
    public async Task Handle_TargetPlanWithoutStripePrice_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, stripePriceIdMonthly: null);
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*not available for online billing*");
    }

    // ── Seed helpers ──────────────────────────────────────────────────────

    private async Task<Plan> SeedPlan(string name, decimal priceMonthly, string? stripePriceIdMonthly)
    {
        Plan plan = new() { Name = name };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Monthly,
            Price = priceMonthly,
            StripePriceId = stripePriceIdMonthly,
        });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    private async Task<Plan> SeedPlanWithBothIntervals(
        string name, decimal priceMonthly, decimal priceYearly, string stripePriceIdMonthly, string stripePriceIdYearly)
    {
        Plan plan = new() { Name = name };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = priceMonthly, StripePriceId = stripePriceIdMonthly });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = priceYearly, StripePriceId = stripePriceIdYearly });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    private async Task SeedSubscription(
        Guid planId, BillingInterval interval, SubscriptionStatus status, string? stripeSubId, Guid? pendingPlanId = null)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = _studioId,
            PlanId = planId,
            BillingInterval = interval,
            PendingPlanId = pendingPlanId,
            Status = status,
            StripeSubscriptionId = stripeSubId,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ImmediateUpgrade_WritesOneExpansionEventFromListPrices()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, "price_pro");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        await CreateSut().Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Expansion);
        ledgerEvent.MrrBefore.Should().Be(29m);   // pre-snapshot subscription: falls back to the current list price
        ledgerEvent.MrrAfter.Should().Be(79m);
        ledgerEvent.PlanId.Should().Be(target.Id);
        ledgerEvent.Source.Should().Be(nameof(ChangePlanHandler));
    }

    [Fact]
    public async Task Handle_ImmediateUpgrade_AppliesExistingRecurringDiscountToBothSides()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, "price_pro");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");
        Subscription seeded = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        seeded.BilledUnitAmount = 29m;
        seeded.BilledQuantity = 1;
        seeded.BilledCurrency = "eur";
        seeded.RecurringDiscountPercent = 50m;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.MrrBefore.Should().Be(14.5m);
        ledgerEvent.MrrAfter.Should().Be(39.5m);
    }

    [Fact]
    public async Task Handle_ImmediateUpgradeFollowedByItsOwnWebhookEcho_WritesExactlyOneExpansion()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target = await SeedPlan("Pro", 79m, "price_pro");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        await CreateSut().Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        // The customer.subscription.updated webhook Stripe sends back for the same upgrade.
        await new HandleSubscriptionUpdatedHandler(_db).Handle(
            new HandleSubscriptionUpdatedCommand(
                "sub_123", "active", _newPeriodEnd, "price_pro", 7900, "eur", 1, null, false, "evt_echo"),
            default);

        _db.SubscriptionRevenueEvents.Should().ContainSingle(e => e.Type == RevenueEventType.Expansion);
        _db.SubscriptionRevenueEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task Handle_ScheduledDowngrade_WritesNoLedgerEventYet()
    {
        Plan current = await SeedPlan("Pro", 79m, "price_pro");
        Plan target = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, BillingInterval.Monthly, SubscriptionStatus.Active, "sub_123");

        await CreateSut().Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id, "Monthly")), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }
}
