using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class UpdatePlanHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _stripe = Substitute.For<IStripeBillingService>();

    private UpdatePlanHandler CreateSut() => new(_db, _stripe, NullLogger<UpdatePlanHandler>.Instance);

    [Fact]
    public async Task Handle_ExistingPlan_UpdatesFields()
    {
        Guid planId = await SeedPlan("Old Name", 49m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "New Name", 17, [new PlanPriceRequest("Monthly", 59m)], AllowBrandingRemoval: false)), default);

        result.Name.Should().Be("New Name");
        result.Prices.Single().Price.Should().Be(59m);
    }

    [Fact]
    public async Task Handle_ExistingPlan_PersistsChanges()
    {
        Guid planId = await SeedPlan("Original", 29m);

        await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Updated", 17, [new PlanPriceRequest("Monthly", 39m)], AllowBrandingRemoval: false)), default);

        _db.Plans.Single(p => p.Id == planId).Name.Should().Be("Updated");
    }

    [Fact]
    public async Task Handle_WithStripePriceIds_UpdatesAndReturnsThem()
    {
        Guid planId = await SeedPlan("Pro", 49m);
        _stripe.GetPriceAsync("price_monthly_new", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 4900, "eur", "month", 1));

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Pro", 17, [new PlanPriceRequest("Monthly", 49m, StripePriceId: "price_monthly_new")],
                AllowBrandingRemoval: false)), default);

        result.Prices.Single().StripePriceId.Should().Be("price_monthly_new");
    }

    [Fact]
    public async Task Handle_NonExistentPlan_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(Guid.NewGuid(), new UpdatePlanRequest(
                "X", 0, [new PlanPriceRequest("Monthly", 10m)], AllowBrandingRemoval: false)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithAllowBrandingRemoval_PersistsAndReturnsFlag()
    {
        Guid planId = await SeedPlan("Premium", 99m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Premium", 15, [new PlanPriceRequest("Monthly", 99m)], AllowBrandingRemoval: true)), default);

        result.AllowBrandingRemoval.Should().BeTrue();
        _db.Plans.Single(p => p.Id == planId).AllowBrandingRemoval.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithLimitFields_PersistsAndReturnsThem()
    {
        Guid planId = await SeedPlan("Growth", 59m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Growth", 17, [new PlanPriceRequest("Monthly", 59m)],
                AllowBrandingRemoval: true,
                MaxArtists: 3,
                MaxAppointmentsPerMonth: 150,
                MaxNotificationsPerMonth: 600,
                MaxStorageGb: 10,
                MaxLocations: 1)), default);

        result.MaxArtists.Should().Be(3);
        result.MaxAppointmentsPerMonth.Should().Be(150);
        _db.Plans.Single(p => p.Id == planId).MaxStorageGb.Should().Be(10);
    }

    [Fact]
    public async Task Handle_AddsYearlyPriceToMonthlyOnlyPlan()
    {
        Guid planId = await SeedPlan("Premium", 79m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Premium", 17,
                [
                    new PlanPriceRequest("Monthly", 79m),
                    new PlanPriceRequest("Yearly", 790m),
                ],
                AllowBrandingRemoval: false)), default);

        result.Prices.Should().HaveCount(2);
        _db.PlanPrices.Count(p => p.PlanId == planId).Should().Be(2);
    }

    [Fact]
    public async Task Handle_OmittedInterval_RemovesExistingPriceRow()
    {
        Plan plan = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17, [new PlanPriceRequest("Monthly", 79m)], AllowBrandingRemoval: false)), default);

        result.Prices.Should().ContainSingle(p => p.Interval == "Monthly");
        _db.PlanPrices.Count(p => p.PlanId == plan.Id).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExistingIntervalPrice_UpdatesInPlace_NotDuplicated()
    {
        Guid planId = await SeedPlan("Pro", 99m);

        await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Pro", 17, [new PlanPriceRequest("Monthly", 129m)], AllowBrandingRemoval: false)), default);

        _db.PlanPrices.Count(p => p.PlanId == planId).Should().Be(1);
        _db.PlanPrices.Single(p => p.PlanId == planId).Price.Should().Be(129m);
    }

    // ── G1: never delete a price in use ──────────────────────────────────

    [Fact]
    public async Task Handle_DropYearlyWithActiveSubscriber_KeepsRowDeactivated()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        await SeedSubscription(plan.Id, BillingInterval.Yearly);

        await CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17, [new PlanPriceRequest("Monthly", 79m)], AllowBrandingRemoval: false)), default);

        PlanPrice yearly = _db.PlanPrices.Single(p => p.PlanId == plan.Id && p.Interval == BillingInterval.Yearly);
        yearly.IsActive.Should().BeFalse();
        _db.PlanPrices.Count(p => p.PlanId == plan.Id).Should().Be(2); // row kept, not removed
    }

    [Fact]
    public async Task Handle_DropYearlyWithScheduledSubscriber_KeepsRowDeactivated()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        await SeedSubscription(plan.Id, BillingInterval.Monthly, pendingPlanId: plan.Id, pendingInterval: BillingInterval.Yearly);

        await CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17, [new PlanPriceRequest("Monthly", 79m)], AllowBrandingRemoval: false)), default);

        PlanPrice yearly = _db.PlanPrices.Single(p => p.PlanId == plan.Id && p.Interval == BillingInterval.Yearly);
        yearly.IsActive.Should().BeFalse();
    }

    // ── G2: no in-place price/Stripe-price change on a price in use ─────

    [Fact]
    public async Task Handle_ChangePriceWithSubscriber_ThrowsAndSavesNothing()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        await SeedSubscription(plan.Id, BillingInterval.Monthly);

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [new PlanPriceRequest("Monthly", 89m), new PlanPriceRequest("Yearly", 790m)],
                AllowBrandingRemoval: false)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*1 subscribed studio(s)*");
        _db.PlanPrices.Single(p => p.PlanId == plan.Id && p.Interval == BillingInterval.Monthly)
            .Price.Should().Be(79m); // nothing saved
    }

    [Fact]
    public async Task Handle_ChangePriceWithSnapshottedSubscriber_Saved()
    {
        // G2 relaxation (Batch 2b): once every subscriber on this price has its own
        // BilledUnitAmount snapshot, changing PlanPrice no longer touches what Stripe bills
        // them — only new sales read the changed row — so the change is now allowed.
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        await SeedSubscription(plan.Id, BillingInterval.Monthly, billedUnitAmount: 79m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [new PlanPriceRequest("Monthly", 89m), new PlanPriceRequest("Yearly", 790m)],
                AllowBrandingRemoval: false)), default);

        result.Prices.Single(p => p.Interval == "Monthly").Price.Should().Be(89m);
    }

    [Fact]
    public async Task Handle_ChangePriceWithOneUnsnapshottedSubscriber_Rejected_NamesBackfillEndpoint()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        await SeedSubscription(plan.Id, BillingInterval.Monthly, billedUnitAmount: 79m);
        await SeedSubscription(plan.Id, BillingInterval.Monthly, billedUnitAmount: null);

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [new PlanPriceRequest("Monthly", 89m), new PlanPriceRequest("Yearly", 790m)],
                AllowBrandingRemoval: false)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*backfill-billed-amounts*");
    }

    [Fact]
    public async Task Handle_ChangeStripePriceIdWithSubscriber_Rejected()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        await SeedSubscription(plan.Id, BillingInterval.Monthly);

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [
                    new PlanPriceRequest("Monthly", 79m, StripePriceId: "price_new"),
                    new PlanPriceRequest("Yearly", 790m),
                ],
                AllowBrandingRemoval: false)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ChangePriceWithNoSubscribers_MatchingStripePrice_Saved()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m);
        _stripe.GetPriceAsync("price_new", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 8900, "eur", "month", 1));

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [
                    new PlanPriceRequest("Monthly", 89m, StripePriceId: "price_new"),
                    new PlanPriceRequest("Yearly", 790m),
                ],
                AllowBrandingRemoval: false)), default);

        result.Prices.Single(p => p.Interval == "Monthly").Price.Should().Be(89m);
        _db.PlanPrices.Single(p => p.PlanId == plan.Id && p.Interval == BillingInterval.Monthly)
            .StripePriceId.Should().Be("price_new");
    }

    // ── G3 re-validates a Price-only edit on an already-linked row too ──

    [Fact]
    public async Task Handle_EditPriceOnLinkedRow_NoSubscribers_StripeStillMatches_Saved()
    {
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m, stripePriceIdMonthly: "price_existing");
        _stripe.GetPriceAsync("price_existing", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 8900, "eur", "month", 1));

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [
                    new PlanPriceRequest("Monthly", 89m, StripePriceId: "price_existing"), // same id, new price
                    new PlanPriceRequest("Yearly", 790m),
                ],
                AllowBrandingRemoval: false)), default);

        result.Prices.Single(p => p.Interval == "Monthly").Price.Should().Be(89m);
    }

    [Fact]
    public async Task Handle_EditPriceOnLinkedRow_NoSubscribers_StripeNowMismatched_Rejected()
    {
        // StripePriceId is left untouched but Price is edited to something Stripe doesn't
        // actually charge — must be caught even though the id itself didn't change.
        Plan plan = await SeedPlanWithBothIntervals("Premium", 79m, 790m, stripePriceIdMonthly: "price_existing");
        _stripe.GetPriceAsync("price_existing", Arg.Any<CancellationToken>())
            .Returns(new StripePriceInfo(true, 7900, "eur", "month", 1)); // still 79, not 89

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(plan.Id, new UpdatePlanRequest(
                "Premium", 17,
                [
                    new PlanPriceRequest("Monthly", 89m, StripePriceId: "price_existing"),
                    new PlanPriceRequest("Yearly", 790m),
                ],
                AllowBrandingRemoval: false)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*79.00*89.00*");
        _db.PlanPrices.Single(p => p.PlanId == plan.Id && p.Interval == BillingInterval.Monthly)
            .Price.Should().Be(79m); // nothing saved
    }

    private async Task<Plan> SeedPlanWithBothIntervals(
        string name, decimal priceMonthly, decimal priceYearly, string? stripePriceIdMonthly = null)
    {
        Plan plan = new() { Name = name };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = priceMonthly, StripePriceId = stripePriceIdMonthly });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = priceYearly });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan;
    }

    private async Task SeedSubscription(
        Guid planId, BillingInterval interval, Guid? pendingPlanId = null, BillingInterval? pendingInterval = null,
        decimal? billedUnitAmount = null)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = Guid.NewGuid(),
            PlanId = pendingPlanId is null ? planId : null,
            BillingInterval = interval,
            PendingPlanId = pendingPlanId,
            PendingBillingInterval = pendingInterval,
            Status = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            BilledUnitAmount = billedUnitAmount,
            BilledCurrency = billedUnitAmount is not null ? "eur" : null,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedPlan(string name, decimal price)
    {
        Plan plan = new() { Name = name };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = price });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan.Id;
    }
}
