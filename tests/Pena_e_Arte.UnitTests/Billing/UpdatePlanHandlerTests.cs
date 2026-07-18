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

    [Fact]
    public async Task Handle_WithLimitFields_PersistsAndReturnsThem()
    {
        Guid planId = await SeedPlan("Growth", 59m);

        PlanResponse result = await CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Growth", 59m, 590m, 17,
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
    public async Task Handle_SelfPairedPlanId_ThrowsBusinessRuleViolationException()
    {
        Guid planId = await SeedPlan("Premium", 79m);

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Premium", 79m, 790m, 17, AllowBrandingRemoval: false, PairedPlanId: planId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PairedPlanIdPointsToNonexistentPlan_ThrowsNotFoundException()
    {
        Guid planId = await SeedPlan("Premium", 79m);

        Func<Task> act = () => CreateSut().Handle(
            new UpdatePlanCommand(planId, new UpdatePlanRequest(
                "Premium", 79m, 790m, 17, AllowBrandingRemoval: false, PairedPlanId: Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_PairedPlanId_PropagatesLimitFieldsToPairedPlan_ButNotPriceOrStripeIds()
    {
        Guid monthlyId = await SeedPlan("Premium", 79m);
        Guid yearlyId  = await SeedPlan("Premium", 79m);

        await CreateSut().Handle(
            new UpdatePlanCommand(monthlyId, new UpdatePlanRequest(
                "Premium", 79m, 790m, 17,
                AllowBrandingRemoval: true,
                StripePriceIdMonthly: "price_monthly_only",
                MaxArtists: 6,
                MaxAppointmentsPerMonth: 400,
                PrioritySupport: true,
                PairedPlanId: yearlyId)), default);

        _db.ChangeTracker.Clear();
        Plan paired = _db.Plans.Single(p => p.Id == yearlyId);

        paired.MaxArtists.Should().Be(6);
        paired.MaxAppointmentsPerMonth.Should().Be(400);
        paired.PrioritySupport.Should().BeTrue();
        paired.AllowBrandingRemoval.Should().BeTrue();
        paired.PairedPlanId.Should().Be(monthlyId, "the link should become symmetric even though only the monthly row set it");
        // Price and Stripe IDs are per-row by design — must NOT have been copied over.
        paired.StripePriceIdMonthly.Should().BeNull();
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
