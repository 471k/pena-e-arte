using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Referrals;

public class ReferralRewardServiceTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly IStripeDiscountService _discounts = Substitute.For<IStripeDiscountService>();

    public ReferralRewardServiceTests()
    {
        _discounts.CreateReferralCouponAsync(Arg.Any<ReferralCouponRequest>(), Arg.Any<CancellationToken>())
                  .Returns("coup_referrer_reward");
        _billing.CreditCustomerBalanceAsync(
                Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("cbtxn_referrer_reward");
    }

    private ReferralRewardService CreateSut() =>
        new(_db, _billing, _discounts, NullLogger<ReferralRewardService>.Instance);

    [Fact]
    public async Task RewardReferrerAsync_MonthlyReferrer_AppliesCoupon()
    {
        (ReferralRedemption redemption, string referrerSubId) =
            await SeedFullReferralScenario(referrerHasStripeSub: true, referrerInterval: BillingInterval.Monthly);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _discounts.Received(1).CreateReferralCouponAsync(
            Arg.Is<ReferralCouponRequest>(r =>
                r.Interval == BillingInterval.Monthly && r.IdempotencyKey.StartsWith("referrer-reward-")),
            Arg.Any<CancellationToken>());

        await _billing.Received(1).ApplyCouponToActiveSubscriptionAsync(
            referrerSubId, "coup_referrer_reward", Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().CreditCustomerBalanceAsync(
            Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        ReferralRedemption updated = _db.ReferralRedemptions.Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeTrue();
        updated.ReferrerRewardCouponId.Should().Be("coup_referrer_reward");
    }

    [Fact]
    public async Task RewardReferrerAsync_YearlyReferrer_CreditsCustomerBalanceInsteadOfCoupon()
    {
        // D6 — a Yearly referrer's next invoice is up to a year away: a repeating coupon
        // would expire unused, so a customer balance credit worth one Monthly price (79
        // for Premium) is applied to the next invoice instead. No coupon at all.
        (ReferralRedemption redemption, string referrerSubId) = await SeedFullReferralScenario(
            referrerHasStripeSub: true, referrerInterval: BillingInterval.Yearly,
            planMonthlyPrice: 79m, planYearlyPrice: 790m);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _billing.Received(1).CreditCustomerBalanceAsync(
            referrerSubId, 79m, Arg.Is<string>(k => k.StartsWith("referrer-reward-")),
            "Referral reward: 1 month free", Arg.Any<CancellationToken>());
        await _discounts.DidNotReceive().CreateReferralCouponAsync(
            Arg.Any<ReferralCouponRequest>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        ReferralRedemption updated = _db.ReferralRedemptions.Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeTrue();
        updated.ReferrerRewardCouponId.Should().Be("cbtxn_referrer_reward");
    }

    [Fact]
    public async Task RewardReferrerAsync_YearlyReferrer_BalanceCreditThrows_LoggedNotAppliedNotRethrown()
    {
        (ReferralRedemption redemption, _) = await SeedFullReferralScenario(
            referrerHasStripeSub: true, referrerInterval: BillingInterval.Yearly,
            planMonthlyPrice: 79m, planYearlyPrice: 790m);
        _billing.CreditCustomerBalanceAsync(
                Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Stripe unavailable"));

        Func<Task> act = () => CreateSut().RewardReferrerAsync(redemption.Id, default);

        await act.Should().NotThrowAsync();
        ReferralRedemption updated = _db.ReferralRedemptions.Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeFalse();
        updated.ReferrerRewardCouponId.Should().BeNull();
    }

    [Fact]
    public async Task RewardReferrerAsync_IsIdempotent_WhenCalledTwice()
    {
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: true, referrerInterval: BillingInterval.Monthly);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);
        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _discounts.Received(1).CreateReferralCouponAsync(
            Arg.Any<ReferralCouponRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewardReferrerAsync_NoOp_WhenReferrerHasNoActiveStripeSub()
    {
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: false, referrerInterval: BillingInterval.Monthly);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _discounts.DidNotReceive().CreateReferralCouponAsync(
            Arg.Any<ReferralCouponRequest>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        ReferralRedemption updated = _db.ReferralRedemptions.Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeFalse();
    }

    [Fact]
    public async Task RewardReferrerAsync_SkipsReward_WhenSelfReferral()
    {
        string sharedEmail = "same@owner.com";
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: true,
                                           referrerOwnerEmail: sharedEmail,
                                           newStudioOwnerEmail: sharedEmail);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.ReferralRedemptions.Single(r => r.Id == redemption.Id)
           .ReferrerRewardApplied.Should().BeFalse();
    }

    private async Task<(ReferralRedemption, string referrerSubId)> SeedFullReferralScenario(
        bool referrerHasStripeSub,
        BillingInterval referrerInterval = BillingInterval.Monthly,
        decimal planMonthlyPrice = 59m,
        decimal? planYearlyPrice = null,
        string referrerOwnerEmail = "referrer@studio.com",
        string newStudioOwnerEmail = "new@studio.com")
    {
        Studio referringStudio = new()
        {
            Id = Guid.NewGuid(),
            Name = "Referring Studio",
            Slug = "ref",
            City = "Porto",
            OwnerEmail = referrerOwnerEmail,
            IsActive = true,
        };

        Studio newStudio = new()
        {
            Id = Guid.NewGuid(),
            Name = "New Studio",
            Slug = "new",
            City = "Lisbon",
            OwnerEmail = newStudioOwnerEmail,
            IsActive = true,
        };

        _db.Studios.AddRange(referringStudio, newStudio);

        ReferralCode code = new()
        {
            StudioId = referringStudio.Id,
            Code = "REFTEST1",
            IsActive = true,
            IsSingleUse = true,
        };
        _db.ReferralCodes.Add(code);

        Plan plan = new() { Name = "Referrer Plan" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = planMonthlyPrice });
        if (planYearlyPrice is decimal yp)
            plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = yp });
        _db.Plans.Add(plan);

        string referrerSubId = "sub_referrer_test";
        if (referrerHasStripeSub)
        {
            _db.Subscriptions.Add(new Subscription
            {
                StudioId = referringStudio.Id,
                PlanId = plan.Id,
                BillingInterval = referrerInterval,
                Status = SubscriptionStatus.Active,
                StripeSubscriptionId = referrerSubId,
                CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            });
        }

        ReferralRedemption redemption = new()
        {
            ReferralCodeId = code.Id,
            NewStudioId = newStudio.Id,
            DiscountApplied = true,
        };
        _db.ReferralRedemptions.Add(redemption);

        await _db.SaveChangesAsync();
        return (redemption, referrerSubId);
    }
}
