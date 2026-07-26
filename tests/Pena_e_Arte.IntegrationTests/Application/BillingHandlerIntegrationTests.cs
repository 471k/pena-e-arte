using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class BillingHandlerIntegrationTests(DatabaseFixture fixture)
{
    // ── CreateSubscription ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSubscription_TrialingStudio_ActivatesSubscriptionInDatabase()
    {
        (Guid studioId, Guid planId) = await SeedStudioWithPlan(SubscriptionStatus.Trialing);

        await RunCreateHandler(studioId, planId);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId);

        sub.Should().NotBeNull();
        sub!.Status.Should().Be(SubscriptionStatus.Active);
        sub.PlanId.Should().Be(planId);
    }

    [Fact]
    public async Task CreateSubscription_GracePeriodStudio_ActivatesSubscriptionInDatabase()
    {
        (Guid studioId, Guid planId) = await SeedStudioWithPlan(SubscriptionStatus.GracePeriod);

        await RunCreateHandler(studioId, planId);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId);

        sub!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task CreateSubscription_AlreadyActive_ThrowsBusinessRuleViolationException()
    {
        (Guid studioId, Guid planId) = await SeedStudioWithPlan(SubscriptionStatus.Active);

        Func<Task> act = () => RunCreateHandler(studioId, planId);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*active*");
    }

    [Fact]
    public async Task CreateSubscription_PlanNotFound_ThrowsNotFoundException()
    {
        Guid studioId = await SeedStudioWithTrialingSubscription();

        Func<Task> act = () => RunCreateHandler(studioId, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateSubscription_NoSubscriptionForStudio_ThrowsNotFoundException()
    {
        Guid planId = await SeedPlan();
        Guid studioId = Guid.NewGuid();

        Func<Task> act = () => RunCreateHandler(studioId, planId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateSubscription_SetsCurrentPeriodEnd()
    {
        (Guid studioId, Guid planId) = await SeedStudioWithPlan(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await RunCreateHandler(studioId, planId);

        result.CurrentPeriodEnd.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateSubscription_FreePlan_ActivatesWithFarFuturePeriodEndAndNoStripeCall()
    {
        Guid planId = await SeedPlan(priceMonthly: 0m);
        Guid studioId = await SeedStudioWithSubscription(SubscriptionStatus.Trialing, planId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ICurrentTenant tenant = TenantFor(studioId);
        IStripeBillingService billing = Substitute.For<IStripeBillingService>();
        IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
        IReferralRewardService rewardService = Substitute.For<IReferralRewardService>();
        CreateSubscriptionHandler handler = new(db, tenant, billing, discounts, rewardService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateSubscriptionHandler>.Instance);

        SubscriptionResponse result = await handler.Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        result.CurrentPeriodEnd.Should().BeAfter(DateTime.UtcNow.AddYears(49));
        result.StripeSubscriptionId.Should().BeNull();

        await billing.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
        await billing.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default!, default, default);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription sub = await verify.Subscriptions.SingleAsync(s => s.StudioId == studioId);
        sub.CurrentPeriodEnd.Should().BeAfter(DateTime.UtcNow.AddYears(49));
        sub.StripeSubscriptionId.Should().BeNull();
    }

    // ── GetSubscription ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSubscription_ExistingSubscription_ReturnsCorrectData()
    {
        (Guid studioId, _) = await SeedStudioWithPlan(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await RunGetHandler(studioId);

        result.StudioId.Should().Be(studioId);
        result.Status.Should().Be(SubscriptionStatus.Trialing.ToString());
    }

    [Fact]
    public async Task GetSubscription_NoSubscription_ThrowsNotFoundException()
    {
        Guid studioId = Guid.NewGuid();

        Func<Task> act = () => RunGetHandler(studioId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetSubscription_SubscriptionFromDifferentStudio_ThrowsNotFoundException()
    {
        (Guid studioA, _) = await SeedStudioWithPlan(SubscriptionStatus.Active);
        Guid studioB = Guid.NewGuid();

        Func<Task> act = () => RunGetHandler(studioB);

        await act.Should().ThrowAsync<NotFoundException>(
            because: "the handler filters by current tenant's studioId");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Guid StudioId, Guid PlanId)> SeedStudioWithPlan(SubscriptionStatus status)
    {
        Guid planId = await SeedPlan();
        Guid studioId = await SeedStudioWithSubscription(status, planId);
        return (studioId, planId);
    }

    private async Task<Guid> SeedStudioWithTrialingSubscription()
    {
        return await SeedStudioWithSubscription(SubscriptionStatus.Trialing, null);
    }

    private async Task<Guid> SeedPlan(decimal priceMonthly = 49m)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);
        Plan plan = new() { Name = priceMonthly == 0 ? "Free" : "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = priceMonthly });
        ctx.Plans.Add(plan);
        await ctx.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<Guid> SeedStudioWithSubscription(SubscriptionStatus status, Guid? planId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Studio studio = new()
        {
            Name = $"Studio {Guid.NewGuid():N}"[..20],
            Slug = $"s-{Guid.NewGuid():N}"[..20],
            City = "Lisboa",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14)
        };
        ctx.Studios.Add(studio);
        await ctx.SaveChangesAsync();

        Subscription sub = new()
        {
            StudioId = studio.Id,
            PlanId = planId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14)
        };
        ctx.Subscriptions.Add(sub);
        await ctx.SaveChangesAsync();

        return studio.Id;
    }

    private async Task<SubscriptionResponse> RunCreateHandler(Guid studioId, Guid planId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ICurrentTenant tenant = TenantFor(studioId);
        IStripeBillingService billing = Substitute.For<IStripeBillingService>();
        billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns("cus_test");
        billing.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(("sub_test", DateTime.UtcNow.AddMonths(1)));
        IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
        IReferralRewardService rewardService = Substitute.For<IReferralRewardService>();
        CreateSubscriptionHandler handler = new(db, tenant, billing, discounts, rewardService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateSubscriptionHandler>.Instance);
        return await handler.Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);
    }

    private async Task<SubscriptionResponse> RunGetHandler(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ICurrentTenant tenant = TenantFor(studioId);
        GetSubscriptionHandler handler = new(db, tenant);
        return await handler.Handle(new GetSubscriptionQuery(), default);
    }

    private static ICurrentTenant TenantFor(Guid studioId)
    {
        CurrentTenantService t = new();
        t.SetTenant(studioId);
        return t;
    }
}
