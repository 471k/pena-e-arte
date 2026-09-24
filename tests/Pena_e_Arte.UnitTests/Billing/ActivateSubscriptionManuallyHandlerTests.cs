using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class ActivateSubscriptionManuallyHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private ActivateSubscriptionManuallyHandler CreateSut() =>
        new(_db, NullLogger<ActivateSubscriptionManuallyHandler>.Instance);

    private async Task<Guid> SeedPlan(decimal monthlyPrice = 49m)
    {
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = monthlyPrice });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = monthlyPrice * 10 });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan.Id;
    }

    private async Task SeedStudio(Subscription? subscription = null)
    {
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Studio",
            Slug = "studio",
            OwnerEmail = "owner@test.com",
        });
        if (subscription is not null)
        {
            subscription.StudioId = _studioId;
            _db.Subscriptions.Add(subscription);
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Handle_NoExistingSubscription_CreatesActiveSubscriptionSnapshottedFromMonthlyPrice()
    {
        Guid planId = await SeedPlan(monthlyPrice: 59m);
        await SeedStudio();

        SubscriptionResponse result = await CreateSut()
            .Handle(new ActivateSubscriptionManuallyCommand(_studioId, planId, null), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        stored.BilledUnitAmount.Should().Be(59m);
        stored.BilledQuantity.Should().Be(1);
        stored.BilledCurrency.Should().Be("eur");
    }

    [Fact]
    public async Task Handle_ExistingSubscription_RefreshesSnapshotFromMonthlyPrice()
    {
        Guid oldPlanId = await SeedPlan(monthlyPrice: 29m);
        await SeedStudio(new Subscription
        {
            PlanId = oldPlanId,
            Status = SubscriptionStatus.Trialing,
            BillingInterval = BillingInterval.Monthly,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
            GracePeriodEnd = DateTime.UtcNow.AddDays(14),
        });
        Guid newPlanId = await SeedPlan(monthlyPrice: 79m);

        await CreateSut().Handle(new ActivateSubscriptionManuallyCommand(_studioId, newPlanId, null), default);

        Subscription stored = _db.Subscriptions.Single(s => s.StudioId == _studioId);
        stored.PlanId.Should().Be(newPlanId);
        stored.BilledUnitAmount.Should().Be(79m);
        stored.BilledCurrency.Should().Be("eur");
    }

    [Fact]
    public async Task Handle_PlanWithNoMonthlyPrice_SnapshotsZero()
    {
        Plan plan = new() { Name = "YearlyOnly" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 490m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await SeedStudio();

        await CreateSut().Handle(new ActivateSubscriptionManuallyCommand(_studioId, plan.Id, null), default);

        _db.Subscriptions.Single(s => s.StudioId == _studioId).BilledUnitAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Guid planId = await SeedPlan();

        Func<Task> act = () => CreateSut()
            .Handle(new ActivateSubscriptionManuallyCommand(Guid.NewGuid(), planId, null), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_PlanNotFound_ThrowsNotFoundException()
    {
        await SeedStudio();

        Func<Task> act = () => CreateSut()
            .Handle(new ActivateSubscriptionManuallyCommand(_studioId, Guid.NewGuid(), null), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Revenue ledger ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoExistingSubscriptionRow_WritesNewLedgerEvent()
    {
        Guid planId = await SeedPlan(monthlyPrice: 59m);
        await SeedStudio();

        await CreateSut().Handle(new ActivateSubscriptionManuallyCommand(_studioId, planId, null), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.New);
        ledgerEvent.MrrBefore.Should().Be(0m);
        ledgerEvent.MrrAfter.Should().Be(59m);
        ledgerEvent.Source.Should().Be(nameof(ActivateSubscriptionManuallyHandler));
    }

    [Fact]
    public async Task Handle_CancelledSubscriptionWithLedgerHistory_WritesReactivation()
    {
        Guid oldPlanId = await SeedPlan(monthlyPrice: 29m);
        await SeedStudio(new Subscription
        {
            PlanId = oldPlanId,
            Status = SubscriptionStatus.Cancelled,
            BillingInterval = BillingInterval.Monthly,
            BilledUnitAmount = 29m,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-7),
        });
        _db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = _db.Subscriptions.Single(s => s.StudioId == _studioId).Id,
            StudioId = _studioId,
            OccurredAt = DateTime.UtcNow.AddMonths(-1),
            Type = RevenueEventType.Churn,
            MrrBefore = 29m,
            MrrAfter = 0m,
            Source = "seed",
            StripeEventId = "seed-churn",
        });
        await _db.SaveChangesAsync();
        Guid newPlanId = await SeedPlan(monthlyPrice: 79m);

        await CreateSut().Handle(new ActivateSubscriptionManuallyCommand(_studioId, newPlanId, null), default);

        SubscriptionRevenueEvent added = _db.SubscriptionRevenueEvents.Single(e => e.Source != "seed");
        added.Type.Should().Be(RevenueEventType.Reactivation);
        added.MrrBefore.Should().Be(0m);   // the cancelled contract was not billing
        added.MrrAfter.Should().Be(79m);
    }

    [Fact]
    public async Task Handle_AlreadyActiveOnCheaperPlan_WritesExpansionNotNew()
    {
        Guid oldPlanId = await SeedPlan(monthlyPrice: 29m);
        await SeedStudio(new Subscription
        {
            PlanId = oldPlanId,
            Status = SubscriptionStatus.Active,
            BillingInterval = BillingInterval.Monthly,
            BilledUnitAmount = 29m,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
        });
        Guid newPlanId = await SeedPlan(monthlyPrice: 79m);

        await CreateSut().Handle(new ActivateSubscriptionManuallyCommand(_studioId, newPlanId, null), default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Expansion);
        ledgerEvent.MrrBefore.Should().Be(29m);
        ledgerEvent.MrrAfter.Should().Be(79m);
    }
}
