using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;
using Xunit;

namespace Pena_e_Arte.UnitTests.Billing;

public class GetPublicPlansHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicPlansHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoPlans_ReturnsEmptyList()
    {
        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PlanWithOnlyInactivePrices_IsExcluded()
    {
        await SeedPlanAsync("Retired", new PlanPrice { Interval = BillingInterval.Monthly, Price = 99m, IsActive = false });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InactivePriceOnLivePlan_IsHiddenButActivePriceIsKept()
    {
        await SeedPlanAsync(
            "Growth",
            new PlanPrice { Interval = BillingInterval.Monthly, Price = 59m },
            new PlanPrice { Interval = BillingInterval.Yearly, Price = 590m, IsActive = false });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        PublicPlanResponse plan = result.Single();
        plan.Prices.Should().ContainSingle();
        plan.Prices.Single().Interval.Should().Be("Monthly");
        plan.Prices.Single().Price.Should().Be(59m);
    }

    [Fact]
    public async Task Handle_MultiplePlans_AreOrderedCheapestFirst()
    {
        await SeedPlanAsync("Premium", new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        await SeedPlanAsync("Free", new PlanPrice { Interval = BillingInterval.Monthly, Price = 0m });
        await SeedPlanAsync("Starter", new PlanPrice { Interval = BillingInterval.Monthly, Price = 29m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Select(p => p.Name).Should().Equal("Free", "Starter", "Premium");
    }

    [Fact]
    public async Task Handle_PlansWithSamePrice_AreOrderedByName()
    {
        await SeedPlanAsync("Beta", new PlanPrice { Interval = BillingInterval.Monthly, Price = 10m });
        await SeedPlanAsync("Alpha", new PlanPrice { Interval = BillingInterval.Monthly, Price = 10m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Select(p => p.Name).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task Handle_Plan_ReportsThePlatformCurrencyInUpperCase()
    {
        await SeedPlanAsync("Starter", new PlanPrice { Interval = BillingInterval.Monthly, Price = 29m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Single().Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_79Monthly790Yearly_MonthsFreeIsTwo()
    {
        await SeedPlanAsync(
            "Premium",
            new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m },
            new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Single().YearlyMonthsFree.Should().Be(2m);
    }

    [Fact]
    public async Task Handle_MonthlyOnlyPlan_MonthsFreeIsNull()
    {
        await SeedPlanAsync("Free", new PlanPrice { Interval = BillingInterval.Monthly, Price = 0m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Single().YearlyMonthsFree.Should().BeNull();
    }

    [Fact]
    public async Task Handle_YearlyThatDoesNotSave_MonthsFreeIsNull()
    {
        await SeedPlanAsync(
            "Odd",
            new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m },
            new PlanPrice { Interval = BillingInterval.Yearly, Price = 1000m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        result.Single().YearlyMonthsFree.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Plan_MapsFeatureFlagsAndLimits_NullMeansUnlimited()
    {
        await SeedPlanAsync(
            "Growth",
            plan =>
            {
                plan.AllowBrandingRemoval = true;
                plan.AllowMarketingCampaigns = true;
                plan.AllowApiAccess = false;
                plan.MaxArtists = 3;
                plan.MaxAppointmentsPerMonth = 150;
                plan.MaxNotificationsPerMonth = 600;
                plan.MaxStorageGb = 10;
                plan.MaxLocations = null;
            },
            new PlanPrice { Interval = BillingInterval.Monthly, Price = 59m });

        List<PublicPlanResponse> result = await CreateSut().Handle(new GetPublicPlansQuery(), default);

        PublicPlanResponse response = result.Single();
        response.AllowBrandingRemoval.Should().BeTrue();
        response.AllowMarketingCampaigns.Should().BeTrue();
        response.AllowApiAccess.Should().BeFalse();
        response.MaxArtists.Should().Be(3);
        response.MaxAppointmentsPerMonth.Should().Be(150);
        response.MaxNotificationsPerMonth.Should().Be(600);
        response.MaxStorageGb.Should().Be(10);
        response.MaxLocations.Should().BeNull();
    }

    [Fact]
    public void ResponseShapes_DoNotExposeInternalFields()
    {
        string[] forbidden = ["Id", "StripePriceId", "SubscriberCount", "PrioritySupport", "IsActive", "YearlyDiscountPercent"];

        IEnumerable<string> exposed = typeof(PublicPlanResponse).GetProperties()
            .Concat(typeof(PublicPlanPriceResponse).GetProperties())
            .Select(p => p.Name);

        exposed.Should().NotContain(forbidden);
    }

    private Task<Plan> SeedPlanAsync(string name, params PlanPrice[] prices) =>
        SeedPlanAsync(name, configure: null, prices);

    private async Task<Plan> SeedPlanAsync(string name, Action<Plan>? configure, params PlanPrice[] prices)
    {
        Plan plan = new() { Id = Guid.NewGuid(), Name = name };
        configure?.Invoke(plan);
        foreach (PlanPrice price in prices)
            plan.Prices.Add(price);
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan;
    }
}
