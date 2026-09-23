using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;
using Xunit;

namespace Pena_e_Arte.UnitTests.Billing;

public class GetPlansHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPlansHandler CreateSut() => new(_db, NullLogger<GetPlansHandler>.Instance);

    [Fact]
    public async Task Handle_NoPlans_ReturnsEmptyList()
    {
        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithPlan_ReturnsZeroSubscribers_WhenNoneExist()
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = "Starter", YearlyDiscountPercent = 17 };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 29m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.Single().SubscriberCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithPlan_ReturnsItsPrices()
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = "Premium", YearlyDiscountPercent = 17 };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.Single().Prices.Should().HaveCount(2);
        result.Single().Prices.Should().Contain(p => p.Interval == "Monthly" && p.Price == 79m);
        result.Single().Prices.Should().Contain(p => p.Interval == "Yearly" && p.Price == 790m);
    }

    [Fact]
    public async Task Handle_WithSubscribers_ReturnsCorrectCount()
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = "Pro", YearlyDiscountPercent = 17 };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.AddRange(
            new Subscription
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                StudioId = Guid.NewGuid(),
                Status = SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            },
            new Subscription
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                StudioId = Guid.NewGuid(),
                Status = SubscriptionStatus.Trialing,
                TrialExpiresAt = DateTime.UtcNow.AddDays(14),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(14),
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.Single().SubscriberCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MultiplePlans_SubscriberCountsArePerPlan()
    {
        Plan planA = new() { Id = Guid.NewGuid(), Name = "A", YearlyDiscountPercent = 17 };
        planA.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 10m });
        Plan planB = new() { Id = Guid.NewGuid(), Name = "B", YearlyDiscountPercent = 17 };
        planB.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 20m });
        _db.Plans.AddRange(planA, planB);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            PlanId = planA.Id,
            StudioId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            TrialExpiresAt = DateTime.UtcNow.AddDays(30),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        _db.Subscriptions.AddRange(
            new Subscription
            {
                Id = Guid.NewGuid(),
                PlanId = planB.Id,
                StudioId = Guid.NewGuid(),
                Status = SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            },
            new Subscription
            {
                Id = Guid.NewGuid(),
                PlanId = planB.Id,
                StudioId = Guid.NewGuid(),
                Status = SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.Single(r => r.Name == "A").SubscriberCount.Should().Be(1);
        result.Single(r => r.Name == "B").SubscriberCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PlanWithOnlyInactivePrices_ExcludedWhenIncludeRetiredFalse()
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = "Pro", YearlyDiscountPercent = 17 };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 99m, IsActive = false });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(IncludeRetired: false), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PlanWithOnlyInactivePrices_IncludedWhenIncludeRetiredTrue()
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = "Pro", YearlyDiscountPercent = 17 };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 99m, IsActive = false });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(IncludeRetired: true), default);

        result.Should().ContainSingle(r => r.Name == "Pro");
    }

    // ── Computed yearly saving (D7, §7.6) ────────────────────────────────────────────

    [Fact]
    public async Task Handle_79Monthly790Yearly_SavingIs158MonthsFreeIs2()
    {
        Plan plan = await SeedPlanWithPrices(79m, 790m);

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        PlanResponse response = result.Single(r => r.Id == plan.Id);
        response.YearlySavingAmount.Should().Be(158m);
        response.YearlyMonthsFree.Should().Be(2m);
    }

    [Fact]
    public async Task Handle_79Monthly800Yearly_SavingIs148MonthsFreeIs1Point87()
    {
        Plan plan = await SeedPlanWithPrices(79m, 800m);

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        PlanResponse response = result.Single(r => r.Id == plan.Id);
        response.YearlySavingAmount.Should().Be(148m);
        response.YearlyMonthsFree.Should().Be(1.87m);
    }

    [Fact]
    public async Task Handle_79MonthlyNoYearly_BothNull()
    {
        Plan plan = await SeedPlanWithPrices(79m, yearly: null);

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        PlanResponse response = result.Single(r => r.Id == plan.Id);
        response.YearlySavingAmount.Should().BeNull();
        response.YearlyMonthsFree.Should().BeNull();
    }

    [Fact]
    public async Task Handle_79Monthly1000Yearly_BothNullSavingNonPositive()
    {
        Plan plan = await SeedPlanWithPrices(79m, 1000m);

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        PlanResponse response = result.Single(r => r.Id == plan.Id);
        response.YearlySavingAmount.Should().BeNull();
        response.YearlyMonthsFree.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ZeroMonthlyNoYearly_BothNull()
    {
        Plan plan = await SeedPlanWithPrices(0m, yearly: null);

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        PlanResponse response = result.Single(r => r.Id == plan.Id);
        response.YearlySavingAmount.Should().BeNull();
        response.YearlyMonthsFree.Should().BeNull();
    }

    private async Task<Plan> SeedPlanWithPrices(decimal monthly, decimal? yearly)
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = $"Plan-{Guid.NewGuid():N}", YearlyDiscountPercent = 17 };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = monthly });
        if (yearly is decimal y)
            plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = y });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan;
    }
}
