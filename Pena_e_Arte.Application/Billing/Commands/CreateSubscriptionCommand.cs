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
    IAppDbContext                          db,
    ICurrentTenant                         tenant,
    IStripeBillingService                  billing,
    IStripeDiscountService                 discounts,
    ILogger<CreateSubscriptionHandler>     logger)
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

        string? priceId = plan.BillingInterval == BillingInterval.Monthly
            ? plan.StripePriceIdMonthly
            : plan.StripePriceIdYearly;

        // Resolve referral discount — re-validate at subscription time as a safety net
        string? couponId      = null;
        bool    discountApplied = false;
        ReferralCode? pendingCode = null;

        if (subscription.Studio?.PendingReferralCodeId is Guid refCodeId)
        {
            pendingCode = await db.ReferralCodes
                .FirstOrDefaultAsync(r => r.Id == refCodeId && r.IsActive, ct);

            if (pendingCode is not null &&
                (pendingCode.ExpiresAt is null || pendingCode.ExpiresAt >= DateTime.UtcNow))
            {
                try
                {
                    couponId = await discounts.CreateOneMonthFreeCouponAsync(ct);
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
            periodEnd = DateTime.UtcNow.AddMonths(1);
        }

        subscription.PlanId           = command.Request.PlanId;
        subscription.Status           = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = periodEnd;

        // Record redemption only when a discount was actually applied
        if (pendingCode is not null && discountApplied)
        {
            db.ReferralRedemptions.Add(new ReferralRedemption
            {
                ReferralCodeId  = pendingCode.Id,
                NewStudioId     = tenant.StudioId,
                DiscountApplied = true,
            });

            if (pendingCode.IsSingleUse)
                pendingCode.IsActive = false;
        }

        // Always clear the pending code regardless of outcome
        if (subscription.Studio?.PendingReferralCodeId is not null)
            subscription.Studio.PendingReferralCodeId = null;

        await db.SaveChangesAsync(ct);

        return Map(subscription);
    }

    internal static SubscriptionResponse Map(Subscription s) => new(
        s.Id, s.StudioId, s.PlanId, s.Status.ToString(),
        s.TrialExpiresAt, s.CurrentPeriodEnd, s.GracePeriodEnd, s.StripeSubscriptionId,
        s.Studio?.StripeAccountId is not null);
}
