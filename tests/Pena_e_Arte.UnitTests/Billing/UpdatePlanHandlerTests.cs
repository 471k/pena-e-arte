using FluentAssertions;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class UpdatePlanHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private UpdatePlanHandler CreateSut() => new(_db);

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
