using FluentAssertions;
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

public class CreateSubscriptionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly IStripeDiscountService _discounts = Substitute.For<IStripeDiscountService>();
    private readonly IReferralRewardService _rewardService = Substitute.For<IReferralRewardService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreateSubscriptionHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns("cus_test123");
        _billing.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(("sub_test123", DateTime.UtcNow.AddMonths(1)));
    }

    private CreateSubscriptionHandler CreateSut() =>
        new(_db, _tenant, _billing, _discounts, _rewardService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateSubscriptionHandler>.Instance);

    [Fact]
    public async Task Handle_ValidPlanAndTrialingSubscription_ReturnsActiveSubscription()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
        result.PlanId.Should().Be(planId);
    }

    [Fact]
    public async Task Handle_ValidPlanAndTrialingSubscription_PersistsActiveStatus()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Trialing);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        _db.Subscriptions.Single(s => s.StudioId == _studioId).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_ValidPlanAndGracePeriodSubscription_ActivatesSubscription()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.GracePeriod);

        SubscriptionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        result.Status.Should().Be(SubscriptionStatus.Active.ToString());
    }

    [Fact]
    public async Task Handle_PlanNotFound_ThrowsNotFoundException()
    {
        await SeedSubscription(SubscriptionStatus.Trialing);

        Func<Task> act = () => CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(Guid.NewGuid(), "Monthly")), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SubscriptionNotFound_ThrowsNotFoundException()
    {
        Guid planId = await SeedPlan();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AlreadyActiveSubscription_ThrowsBusinessRuleViolationException()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Active);

        Func<Task> act = () => CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*active*");
    }

    [Fact]
    public async Task Handle_PlanWithStripePriceId_CallsStripeAndStoresSubscriptionId()
    {
        Guid planId = await SeedPlan(stripePriceIdMonthly: "price_monthly_abc");
        await SeedSubscription(SubscriptionStatus.Trialing);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _billing.Received(1).CreateSubscriptionAsync(
            Arg.Any<string>(), "price_monthly_abc", Arg.Any<string?>(), Arg.Any<CancellationToken>());

        _db.Subscriptions.Single(s => s.StudioId == _studioId)
            .StripeSubscriptionId.Should().Be("sub_test123");
    }

    [Fact]
    public async Task Handle_PlanWithStripePriceId_CreatesStripeCustomerWhenMissing()
    {
        Guid planId = await SeedPlan(stripePriceIdMonthly: "price_monthly_abc");
        await SeedSubscription(SubscriptionStatus.Trialing);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _billing.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Studios.Single(s => s.Id == _studioId).StripeCustomerId.Should().Be("cus_test123");
    }

    [Fact]
    public async Task Handle_PlanWithoutStripePriceId_DoesNotCallStripe()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(SubscriptionStatus.Trialing);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _billing.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FreePlan_SetsFarFuturePeriodEnd()
    {
        Guid planId = await SeedPlan(priceMonthly: 0m);
        await SeedSubscription(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        result.CurrentPeriodEnd.Should().BeAfter(DateTime.UtcNow.AddYears(49));
    }

    [Fact]
    public async Task Handle_PaidPlanWithoutStripePriceId_SetsOneMonthPeriodEnd()
    {
        Guid planId = await SeedPlan(priceMonthly: 49m);
        await SeedSubscription(SubscriptionStatus.Trialing);

        SubscriptionResponse result = await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        result.CurrentPeriodEnd.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Handle_FreePlan_SkipsReferralCoupon()
    {
        Guid planId = await SeedPlan(priceMonthly: 0m);
        Guid referralCodeId = await SeedActiveReferralCode();
        await SeedSubscription(SubscriptionStatus.Trialing, pendingReferralCodeId: referralCodeId);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _discounts.DidNotReceive().CreateOneMonthFreeCouponAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SoloStudioUpgradingToPaidPlan_KeepsIsSolo()
    {
        // IsSolo is never cleared by a plan change — AcceptStudioJoinInviteCommand and
        // InviteSoloArtistToJoinCommand both hard-gate on it, so a studio that's still
        // functionally solo but paying for a bigger plan must not lose join-invite eligibility.
        Guid planId = await SeedPlan(priceMonthly: 49m);
        await SeedSubscription(SubscriptionStatus.Trialing, isSolo: true);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        _db.Studios.Single(s => s.Id == _studioId).IsSolo.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SoloStudioStayingOnFreePlan_KeepsIsSolo()
    {
        Guid planId = await SeedPlan(priceMonthly: 0m);
        await SeedSubscription(SubscriptionStatus.Trialing, isSolo: true);

        await CreateSut()
            .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        _db.Studios.Single(s => s.Id == _studioId).IsSolo.Should().BeTrue();
    }

    private async Task<Guid> SeedPlan(string? stripePriceIdMonthly = null, decimal priceMonthly = 49m)
    {
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Monthly,
            Price = priceMonthly,
            StripePriceId = stripePriceIdMonthly,
        });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<Guid> SeedActiveReferralCode()
    {
        ReferralCode code = new()
        {
            StudioId = Guid.NewGuid(),
            Code = "REFCODE1",
            IsActive = true,
        };
        _db.ReferralCodes.Add(code);
        await _db.SaveChangesAsync();
        return code.Id;
    }

    private async Task SeedSubscription(
        SubscriptionStatus status, Guid? pendingReferralCodeId = null, bool isSolo = false)
    {
        Studio studio = new()
        {
            Id = _studioId,
            Name = "Test Studio",
            Slug = "test-studio",
            City = "Lisboa",
            OwnerEmail = "owner@test.com",
            IsActive = true,
            IsSolo = isSolo,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
            PendingReferralCodeId = pendingReferralCodeId,
        };
        _db.Studios.Add(studio);

        _db.Subscriptions.Add(new Subscription
        {
            StudioId = _studioId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14)
        });
        await _db.SaveChangesAsync();
    }
}
