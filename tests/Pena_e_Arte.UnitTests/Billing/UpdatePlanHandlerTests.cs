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
            new UpdatePlanCommand(planId, new UpdatePlanRequest("New Name", 59m, 590m, 17, AllowBrandingRemoval: false)), default);

        result.Name.Should().Be("New Name");
        result.PriceMonthly.Should().Be(59m);
    }

    [Fact]
    public async Task Handle_ExistingPlan_PersistsChanges()
    {
        Guid planId = await SeedPlan("Original", 29m);

        await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest("Updated", 39m, 390m, 17, AllowBrandingRemoval: false)), default);

        _db.Plans.Single(p => p.Id == planId).Name.Should().Be("Updated");
    }

    [Fact]
    public async Task Handle_WithStripePriceIds_UpdatesAndReturnsThem()
    {
        Guid planId = await SeedPlan("Pro", 49m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Pro", 49m, 490m, 17,
                AllowBrandingRemoval: false,
                StripePriceIdMonthly: "price_monthly_new",
                StripePriceIdYearly:  "price_yearly_new")), default);

        result.StripePriceIdMonthly.Should().Be("price_monthly_new");
        result.StripePriceIdYearly.Should().Be("price_yearly_new");
    }

    [Fact]
    public async Task Handle_NonExistentPlan_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(Guid.NewGuid(), new UpdatePlanRequest("X", 10m, 100m, 0, AllowBrandingRemoval: false)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithAllowBrandingRemoval_PersistsAndReturnsFlag()
    {
        Guid planId = await SeedPlan("Premium", 99m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest("Premium", 99m, 990m, 15, AllowBrandingRemoval: true)), default);

        result.AllowBrandingRemoval.Should().BeTrue();
        _db.Plans.Single(p => p.Id == planId).AllowBrandingRemoval.Should().BeTrue();
    }

    private async Task<Guid> SeedPlan(string name, decimal price)
    {
        Plan plan = new()
        {
            Name            = name,
            BillingInterval = BillingInterval.Monthly,
            PriceMonthly    = price,
            PriceYearly     = price * 10
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return plan.Id;
    }
}
