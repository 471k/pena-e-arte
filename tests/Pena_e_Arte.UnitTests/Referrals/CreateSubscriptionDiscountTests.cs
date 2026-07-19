using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Referrals;

public class CreateSubscriptionDiscountTests
{
    private readonly FakeDbContext          _db        = FakeDbContext.Create();
    private readonly ICurrentTenant         _tenant    = Substitute.For<ICurrentTenant>();
    private readonly IStripeBillingService  _billing   = Substitute.For<IStripeBillingService>();
    private readonly IStripeDiscountService _discounts = Substitute.For<IStripeDiscountService>();
    private readonly IReferralRewardService _rewardService = Substitute.For<IReferralRewardService>();
    private readonly Guid                   _studioId  = Guid.NewGuid();

    public CreateSubscriptionDiscountTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns("cus_ref_test");
        _billing.CreateSubscriptionAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(("sub_ref_test", DateTime.UtcNow.AddMonths(1)));
        _discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns("coup_free1m");
    }

    private CreateSubscriptionHandler CreateSut() =>
        new(_db, _tenant, _billing, _discounts, _rewardService,
            NullLogger<CreateSubscriptionHandler>.Instance);

    [Fact]
    public async Task Handle_WithValidPendingReferralCode_AppliesDiscountAndCreatesRedemption()
    {
        Guid planId = await SeedPlan(stripePriceId: "price_monthly_ref");
        ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: null);
        await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _discounts.Received(1).CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _billing.Received(1).CreateSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), "coup_free1m", Arg.Any<CancellationToken>());

        _db.ReferralRedemptions.Should().ContainSingle(r =>
            r.ReferralCodeId == code.Id &&
            r.NewStudioId    == _studioId &&
            r.DiscountApplied);
    }

    [Fact]
    public async Task Handle_WithExpiredReferralCode_SkipsDiscountAndNoRedemption()
    {
        Guid planId = await SeedPlan();
        ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: DateTime.UtcNow.AddDays(-1));
        await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _discounts.DidNotReceive().CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.ReferralRedemptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithInactiveReferralCode_SkipsDiscountAndNoRedemption()
    {
        Guid planId = await SeedPlan();
        ReferralCode code = await SeedReferralCode(isActive: false, expiresAt: null);
        await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _discounts.DidNotReceive().CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.ReferralRedemptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SingleUseCode_DeactivatesAfterRedemption()
    {
        Guid planId = await SeedPlan(stripePriceId: null);
        ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: null, isSingleUse: true);
        await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        _db.ReferralCodes.Single(r => r.Id == code.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithValidPendingReferralCode_CallsRewardReferrerAsync()
    {
        Guid planId = await SeedPlan(stripePriceId: "price_monthly_ref");
        ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: null);
        await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _rewardService.Received(1).RewardReferrerAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutDiscount_DoesNotCallRewardReferrerAsync()
    {
        Guid planId = await SeedPlan();
        ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: DateTime.UtcNow.AddDays(-1));
        await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _rewardService.DidNotReceive().RewardReferrerAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutPendingReferralCode_DoesNotCallDiscountService()
    {
        Guid planId = await SeedPlan();
        await SeedSubscription(planId: null, pendingReferralCodeId: null);

        await CreateSut().Handle(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId, "Monthly")), default);

        await _discounts.DidNotReceive().CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.ReferralRedemptions.Should().BeEmpty();
    }

    private async Task<Guid> SeedPlan(string? stripePriceId = null)
    {
        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m, StripePriceId = stripePriceId });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<ReferralCode> SeedReferralCode(bool isActive, DateTime? expiresAt, bool isSingleUse = true)
    {
        ReferralCode code = new()
        {
            StudioId    = Guid.NewGuid(),
            Code        = "REF00001",
            IsActive    = isActive,
            IsSingleUse = isSingleUse,
            ExpiresAt   = expiresAt,
        };
        _db.ReferralCodes.Add(code);
        await _db.SaveChangesAsync();
        return code;
    }

    private async Task SeedSubscription(Guid? planId, Guid? pendingReferralCodeId)
    {
        Studio studio = new()
        {
            Id                    = _studioId,
            Name                  = "Referral Test Studio",
            Slug                  = "ref-test",
            City                  = "Porto",
            OwnerEmail            = "owner@ref.com",
            IsActive              = true,
            TrialExpiresAt        = DateTime.UtcNow.AddDays(14),
            PendingReferralCodeId = pendingReferralCodeId,
        };
        _db.Studios.Add(studio);
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = _studioId,
            PlanId           = planId,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14),
        });
        await _db.SaveChangesAsync();
    }
}
