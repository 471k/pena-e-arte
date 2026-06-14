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
    private readonly FakeDbContext         _db      = FakeDbContext.Create();
    private readonly ICurrentTenant        _tenant  = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly Guid                  _studioId = Guid.NewGuid();

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
        Plan target  = await SeedPlan("Pro",   79m, "price_pro");
        await SeedSubscription(current.Id, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id)), default);

        result.PlanId.Should().Be(target.Id);
        result.PendingPlanId.Should().BeNull();
        result.CurrentPeriodEnd.Should().BeCloseTo(_newPeriodEnd, TimeSpan.FromSeconds(1));
        await _billing.Received(1).ChangeSubscriptionPriceAsync("sub_123", "price_pro", Arg.Any<CancellationToken>());
    }

    // ── Downgrades ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DowngradeToCheaperPlan_SchedulesChangeAtPeriodEnd()
    {
        Plan current = await SeedPlan("Pro",   79m, "price_pro");
        Plan target  = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, SubscriptionStatus.Active, "sub_123");

        SubscriptionResponse result = await CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id)), default);

        result.PlanId.Should().Be(current.Id);          // still on the current plan
        result.PendingPlanId.Should().Be(target.Id);    // change is pending
        await _billing.Received(1).ScheduleSubscriptionPriceChangeAsync(
            "sub_123", "price_pro", "price_basic", "month", Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ChangeSubscriptionPriceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SubscriptionNotActive_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target  = await SeedPlan("Pro",   79m, "price_pro");
        await SeedSubscription(current.Id, SubscriptionStatus.Trialing, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*active subscription*");
    }

    [Fact]
    public async Task Handle_CashBilledSubscription_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target  = await SeedPlan("Pro",   79m, "price_pro");
        await SeedSubscription(current.Id, SubscriptionStatus.Active, stripeSubId: null);

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*billed outside Stripe*");
    }

    [Fact]
    public async Task Handle_PendingChangeAlreadyScheduled_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Pro",   79m, "price_pro");
        Plan pending = await SeedPlan("Basic", 29m, "price_basic");
        Plan target  = await SeedPlan("Studio", 129m, "price_studio");
        await SeedSubscription(current.Id, SubscriptionStatus.Active, "sub_123", pendingPlanId: pending.Id);

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already scheduled*");
    }

    [Fact]
    public async Task Handle_SamePlan_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, SubscriptionStatus.Active, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(current.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already on this plan*");
    }

    [Fact]
    public async Task Handle_PlanNotFound_ThrowsNotFoundException()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        await SeedSubscription(current.Id, SubscriptionStatus.Active, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_TargetPlanWithoutStripePrice_ThrowsBusinessRuleViolation()
    {
        Plan current = await SeedPlan("Basic", 29m, "price_basic");
        Plan target  = await SeedPlan("Pro",   79m, stripePriceIdMonthly: null);
        await SeedSubscription(current.Id, SubscriptionStatus.Active, "sub_123");

        Func<Task> act = () => CreateSut()
            .Handle(new ChangePlanCommand(new ChangePlanRequest(target.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*not available for online billing*");
    }

    // ── Seed helpers ──────────────────────────────────────────────────────

    private async Task<Plan> SeedPlan(string name, decimal priceMonthly, string? stripePriceIdMonthly)
    {
        Plan plan = new()
        {
            Name                 = name,
            BillingInterval      = BillingInterval.Monthly,
            PriceMonthly         = priceMonthly,
            PriceYearly          = priceMonthly * 10,
            StripePriceIdMonthly = stripePriceIdMonthly,
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    private async Task SeedSubscription(
        Guid planId, SubscriptionStatus status, string? stripeSubId, Guid? pendingPlanId = null)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId             = _studioId,
            PlanId               = planId,
            PendingPlanId        = pendingPlanId,
            Status               = status,
            StripeSubscriptionId = stripeSubId,
            TrialExpiresAt       = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd     = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd       = DateTime.UtcNow.AddDays(-13),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
