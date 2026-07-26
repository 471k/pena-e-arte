using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class ReferralRewardService(
    IAppDbContext db,
    IStripeBillingService billing,
    IStripeDiscountService discounts,
    ILogger<ReferralRewardService> logger)
    : IReferralRewardService
{
    public async Task RewardReferrerAsync(Guid referralRedemptionId, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #26 — ReferralRewardService loads the
        // referring studio's subscription cross-tenant. The referred studio just
        // subscribed (its tenant context is active), but the referrer is a different
        // tenant. No PII is written to any log statement below. See architecture.md
        // Decisions Log entry "IgnoreQueryFilters #26".
        ReferralRedemption? redemption = await db.ReferralRedemptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == referralRedemptionId, ct)
            ?? throw new NotFoundException(nameof(ReferralRedemption), referralRedemptionId);

        // Idempotency guard — a retry from either subscription-creation path must not
        // issue a second coupon for the same redemption.
        if (redemption.ReferrerRewardApplied)
        {
            logger.LogInformation(
                "Referrer reward already applied for redemption {@RedemptionId}; skipping.",
                referralRedemptionId);
            return;
        }

        ReferralCode? code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == redemption.ReferralCodeId, ct);

        if (code is null)
        {
            logger.LogWarning(
                "Referral code for redemption {@RedemptionId} not found; skipping reward.",
                referralRedemptionId);
            return;
        }

        // ── Self-referral fraud check ─────────────────────────────────────────
        // Compare owner emails in memory. Never log the email values — log only IDs.
        // TODO(product): self-referral policy — currently logs and skips reward;
        //                confirm whether to block silently, flag for support review,
        //                or rate-limit before merging.
        Studio? newStudio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == redemption.NewStudioId, ct);

        Studio? referringStudio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == code.StudioId, ct);

        if (newStudio is not null && referringStudio is not null &&
            string.Equals(referringStudio.OwnerEmail, newStudio.OwnerEmail,
                          StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Self-referral detected for redemption {@RedemptionId}: referring studio " +
                "{@ReferrerStudioId} and new studio {@NewStudioId} share an owner. " +
                "Reward skipped — review recommended.",
                referralRedemptionId, referringStudio.Id, redemption.NewStudioId);
            return;
        }

        // ── Referrer's active Stripe subscription ────────────────────────────
        Subscription? referrerSub = await db.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s =>
                s.StudioId == code.StudioId &&
                s.Status == SubscriptionStatus.Active &&
                s.StripeSubscriptionId != null, ct);

        if (referrerSub?.StripeSubscriptionId is null)
        {
            // TODO(product): referrer has no active Stripe subscription (Trialing,
            // cash-billed, Free tier, or cancelled). Decide whether to:
            //   (a) queue a pending reward to apply on their next real Stripe subscribe
            //   (b) handle via support manually
            //   (c) not reward this case at all
            // Until that decision is made, log and return — don't silently discard.
            logger.LogWarning(
                "Referrer studio {@ReferrerStudioId} has no active Stripe subscription; " +
                "reward not applied for redemption {@RedemptionId}. Manual review may be required.",
                code.StudioId, referralRedemptionId);
            return;
        }

        // ── Issue and apply the coupon ────────────────────────────────────────
        // Idempotency key scoped to THIS redemption, distinct from the referred
        // studio's coupon key ("referral-coupon-{studioId}") to avoid collision.
        string idempotencyKey = $"referrer-reward-{referralRedemptionId}";

        string couponId;
        try
        {
            couponId = await discounts.CreateOneMonthFreeCouponAsync(idempotencyKey, ct);
        }
        catch (Exception ex)
        {
            // Coupon creation failure must not roll back or corrupt the referred
            // studio's subscription, which is already committed. Log and return.
            logger.LogError(ex,
                "Failed to create referrer reward coupon for redemption {@RedemptionId}; " +
                "subscription unaffected. Referrer studio: {@ReferrerStudioId}.",
                referralRedemptionId, code.StudioId);
            return;
        }

        try
        {
            await billing.ApplyCouponToActiveSubscriptionAsync(
                referrerSub.StripeSubscriptionId, couponId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to apply referrer reward coupon {@CouponId} to Stripe subscription " +
                "for redemption {@RedemptionId}. Referrer studio: {@ReferrerStudioId}. " +
                "Coupon was created and should be applied manually.",
                couponId, referralRedemptionId, code.StudioId);
            return;
        }

        redemption.ReferrerRewardApplied = true;
        redemption.ReferrerRewardCouponId = couponId;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Referrer reward applied for redemption {@RedemptionId}. " +
            "Referrer studio {@ReferrerStudioId} received coupon.",
            referralRedemptionId, code.StudioId);
    }
}
