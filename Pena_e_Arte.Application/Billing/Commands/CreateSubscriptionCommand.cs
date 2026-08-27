using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

public record CreateSubscriptionCommand(CreateSubscriptionRequest Request) : IRequest<SubscriptionResponse>;

public class CreateSubscriptionHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IStripeBillingService billing,
    IStripeDiscountService discounts,
    IReferralRewardService rewardService,
    ILogger<CreateSubscriptionHandler> logger)
    : IRequestHandler<CreateSubscriptionCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(CreateSubscriptionCommand command, CancellationToken ct)
    {
        Domain.Entities.Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.Request.PlanId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Plan), command.Request.PlanId);

        Subscription subscription = await db.Subscriptions
            .Include(s => s.Studio)
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        if (subscription.Status == SubscriptionStatus.Active)
            throw new BusinessRuleViolationException("Studio already has an active subscription.");

        BillingInterval requestedInterval =
            Enum.Parse<BillingInterval>(command.Request.BillingInterval, ignoreCase: true);

        PlanPrice price = await db.PlanPrices
            .FirstOrDefaultAsync(pp => pp.PlanId == plan.Id && pp.Interval == requestedInterval && pp.IsActive, ct)
            ?? throw new BusinessRuleViolationException(
                "This plan is not available at that billing interval. Please contact the platform.");

        string? priceId = price.StripePriceId;

        // Resolve referral discount — re-validate at subscription time as a safety net
        string? couponId = null;
        bool discountApplied = false;
        ReferralCode? pendingCode = null;

        // A Free plan (price == 0) has nothing to discount — skip coupon creation
        // entirely rather than let it fail harmlessly into the catch block below.
        if (price.Price > 0 && subscription.Studio?.PendingReferralCodeId is Guid refCodeId)
        {
            pendingCode = await db.ReferralCodes
                .FirstOrDefaultAsync(r => r.Id == refCodeId && r.IsActive, ct);

            if (pendingCode is not null &&
                (pendingCode.ExpiresAt is null || pendingCode.ExpiresAt >= DateTime.UtcNow))
            {
                try
                {
                    // Idempotency key ensures retries don't create a second coupon for the same studio.
                    string idempotencyKey = $"referral-coupon-{tenant.StudioId}";
                    couponId = await discounts.CreateOneMonthFreeCouponAsync(idempotencyKey, ct);
                    discountApplied = true;
                    logger.LogInformation(
                        "Applying referral discount via coupon for studio {@StudioId} from code {@ReferralCodeId}",
                        tenant.StudioId, refCodeId);
                }
                catch (Exception ex)
                {
                    // Discount failure is non-fatal — subscription still created
                    logger.LogWarning(ex,
                        "Failed to create referral coupon for studio {@StudioId}; continuing without discount",
                        tenant.StudioId);
                }
            }
            else
            {
                logger.LogInformation(
                    "Referral code {@ReferralCodeId} is no longer valid at subscription time; skipping discount",
                    refCodeId);
            }
        }

        DateTime periodEnd;

        if (priceId is not null && subscription.Studio is not null)
        {
            if (subscription.Studio.StripeCustomerId is null)
            {
                subscription.Studio.StripeCustomerId =
                    await billing.CreateCustomerAsync(subscription.Studio.OwnerEmail, ct);
            }

            (string stripeSubId, periodEnd) = await billing.CreateSubscriptionAsync(
                subscription.Studio.StripeCustomerId!, priceId, couponId, ct);

            subscription.StripeSubscriptionId = stripeSubId;
        }
        else
        {
            // Free plan (price == 0): never expires — use a far-future sentinel so it
            // stays permanently in the Active pass-through state. This is deliberately
            // NOT the same "+1 month" used for genuinely cash-billed paid plans below: a
            // future recurring expiry job built for the cash-billing case must not sweep
            // Free-tier studios into it.
            periodEnd = price.Price == 0
                ? DateTime.UtcNow.AddYears(50)
                : DateTime.UtcNow.AddMonths(1);
        }

        subscription.PlanId = command.Request.PlanId;
        subscription.BillingInterval = requestedInterval;
        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = periodEnd;
        subscription.TrialExpiresAt = null;

        // A solo studio upgrading off the Free plan stops describing itself as "solo" —
        // purely cosmetic/analytics, does not gate anything (a solo studio already has
        // full functional access; IsPublished is unaffected).
        if (subscription.Studio is { IsSolo: true } && price.Price > 0)
            subscription.Studio.IsSolo = false;

        // Record redemption only when a discount was actually applied
        ReferralRedemption? newRedemption = null;
        if (pendingCode is not null && discountApplied)
        {
            newRedemption = new ReferralRedemption
            {
                ReferralCodeId = pendingCode.Id,
                NewStudioId = tenant.StudioId,
                DiscountApplied = true,
            };
            db.ReferralRedemptions.Add(newRedemption);

            if (pendingCode.IsSingleUse)
                pendingCode.IsActive = false;
        }

        // Always clear the pending code regardless of outcome
        if (subscription.Studio?.PendingReferralCodeId is not null)
            subscription.Studio.PendingReferralCodeId = null;

        await db.SaveChangesAsync(ct);

        // Reward the referrer now that the referred studio's discount is committed.
        // Non-fatal: failure is logged inside RewardReferrerAsync; subscription is not rolled back.
        if (newRedemption is not null)
            await rewardService.RewardReferrerAsync(newRedemption.Id, ct);

        return Map(subscription);
    }

    internal static SubscriptionResponse Map(Subscription s) => new(
        s.Id, s.StudioId, s.PlanId, s.BillingInterval.ToString(),
        s.PendingPlanId, s.PendingBillingInterval?.ToString(), s.Status.ToString(),
        s.TrialExpiresAt, s.CurrentPeriodEnd, s.GracePeriodEnd, s.StripeSubscriptionId,
        s.CancelAtPeriodEnd);
}
