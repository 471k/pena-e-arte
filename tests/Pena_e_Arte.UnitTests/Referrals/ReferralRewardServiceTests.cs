using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Referrals;

public class ReferralRewardServiceTests
{
    private readonly FakeDbContext           _db        = FakeDbContext.Create();
    private readonly IStripeBillingService   _billing   = Substitute.For<IStripeBillingService>();
    private readonly IStripeDiscountService  _discounts = Substitute.For<IStripeDiscountService>();

    public ReferralRewardServiceTests()
    {
        _discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns("coup_referrer_reward");
    }

    private ReferralRewardService CreateSut() =>
        new(_db, _billing, _discounts, NullLogger<ReferralRewardService>.Instance);

    [Fact]
    public async Task RewardReferrerAsync_AppliesCoupon_WhenReferrerHasActiveStripeSubscription()
    {
        (ReferralRedemption redemption, string referrerSubId) =
            await SeedFullReferralScenario(referrerHasStripeSub: true);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _discounts.Received(1).CreateOneMonthFreeCouponAsync(
            Arg.Is<string>(k => k.StartsWith("referrer-reward-")),
            Arg.Any<CancellationToken>());

        await _billing.Received(1).ApplyCouponToActiveSubscriptionAsync(
            referrerSubId, "coup_referrer_reward", Arg.Any<CancellationToken>());

        ReferralRedemption updated = _db.ReferralRedemptions.Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeTrue();
        updated.ReferrerRewardCouponId.Should().Be("coup_referrer_reward");
    }

    [Fact]
    public async Task RewardReferrerAsync_IsIdempotent_WhenCalledTwice()
    {
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: true);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);
        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _discounts.Received(1).CreateOneMonthFreeCouponAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewardReferrerAsync_NoOp_WhenReferrerHasNoActiveStripeSub()
    {
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: false);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _discounts.DidNotReceive().CreateOneMonthFreeCouponAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        bool   referrerHasStripeSub,
        string referrerOwnerEmail   = "referrer@studio.com",
        string newStudioOwnerEmail  = "new@studio.com")
    {
        Studio referringStudio = new()
        {
            Id         = Guid.NewGuid(),
            Name       = "Referring Studio",
            Slug       = "ref",
            City       = "Porto",
            OwnerEmail = referrerOwnerEmail,
            IsActive   = true,
        };

        Studio newStudio = new()
        {
            Id         = Guid.NewGuid(),
            Name       = "New Studio",
            Slug       = "new",
            City       = "Lisbon",
            OwnerEmail = newStudioOwnerEmail,
            IsActive   = true,
        };

        _db.Studios.AddRange(referringStudio, newStudio);

        ReferralCode code = new()
        {
            StudioId    = referringStudio.Id,
            Code        = "REFTEST1",
            IsActive    = true,
            IsSingleUse = true,
        };
        _db.ReferralCodes.Add(code);

        string referrerSubId = "sub_referrer_test";
        if (referrerHasStripeSub)
        {
            _db.Subscriptions.Add(new Subscription
            {
                StudioId             = referringStudio.Id,
                Status               = SubscriptionStatus.Active,
                StripeSubscriptionId = referrerSubId,
                CurrentPeriodEnd     = DateTime.UtcNow.AddMonths(1),
            });
        }

        ReferralRedemption redemption = new()
        {
            ReferralCodeId  = code.Id,
            NewStudioId     = newStudio.Id,
            DiscountApplied = true,
        };
        _db.ReferralRedemptions.Add(redemption);

        await _db.SaveChangesAsync();
        return (redemption, referrerSubId);
    }
}
