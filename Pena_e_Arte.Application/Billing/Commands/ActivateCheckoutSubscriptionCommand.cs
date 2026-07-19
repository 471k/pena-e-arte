using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

/// <summary>
/// Activates a studio's subscription from a completed Stripe Checkout session.
/// Fired from the Stripe webhook (no tenant context) and from the finalize endpoint
/// on the owner's return (tenant context). Idempotent and reconciled from Stripe, so
/// it works even when the webhook is missed. Returns null while the session is not yet
/// paid/complete. <paramref name="ExpectedStudioId"/> guards the finalize path so an
/// owner can only finalize their own studio's session.
/// </summary>
public record ActivateCheckoutSubscriptionCommand(string SessionId, Guid? ExpectedStudioId)
    : IRequest<SubscriptionResponse?>;

public class ActivateCheckoutSubscriptionHandler(
    IAppDbContext                                  db,
    IStripeBillingService                          billing,
    IReferralRewardService                         rewardService,
    ILogger<ActivateCheckoutSubscriptionHandler>   logger)
    : IRequestHandler<ActivateCheckoutSubscriptionCommand, SubscriptionResponse?>
{
    public async Task<SubscriptionResponse?> Handle(ActivateCheckoutSubscriptionCommand command, CancellationToken ct)
    {
        CheckoutSubscriptionResult? result = await billing.GetCheckoutSubscriptionAsync(command.SessionId, ct);

        // Not finished/paid yet — the webhook or a later finalize will pick it up.
        if (result is null || !result.IsComplete || result.StripeSubscriptionId is null)
            return null;

        if (!Guid.TryParse(result.ClientReferenceId, out Guid studioId))
        {
            logger.LogWarning("Checkout session {@SessionId} has no studio reference.", command.SessionId);
            return null;
        }

        // Finalize endpoint passes the caller's tenant; never activate another studio's session.
        if (command.ExpectedStudioId is Guid expected && expected != studioId)
            throw new NotFoundException(nameof(Subscription), command.SessionId);

        Subscription subscription = await db.Subscriptions
            .Include(s => s.Studio)
            .FirstOrDefaultAsync(s => s.StudioId == studioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), studioId);

        // Idempotent: already linked & active.
        if (subscription.Status == SubscriptionStatus.Active
            && subscription.StripeSubscriptionId == result.StripeSubscriptionId)
            return CreateSubscriptionHandler.Map(subscription);

        Domain.Entities.Plan? plan = result.PriceId is null
            ? null
            : await db.Plans.FirstOrDefaultAsync(
                p => p.StripePriceIdMonthly == result.PriceId || p.StripePriceIdYearly == result.PriceId, ct);

        if (subscription.Studio is not null && result.StripeCustomerId is not null)
            subscription.Studio.StripeCustomerId = result.StripeCustomerId;

        subscription.StripeSubscriptionId = result.StripeSubscriptionId;
        if (plan is not null) subscription.PlanId = plan.Id;
        subscription.Status               = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd     = result.CurrentPeriodEnd;
        subscription.TrialExpiresAt       = null;

        ReferralRedemption? newRedemption =
            await RecordReferralRedemptionAsync(subscription.Studio, result.HasDiscount, ct);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Subscription activated via checkout for studio {@StudioId}", studioId);

        // Reward the referrer if the referred studio's discount was applied.
        if (newRedemption is { DiscountApplied: true })
            await rewardService.RewardReferrerAsync(newRedemption.Id, ct);

        return CreateSubscriptionHandler.Map(subscription);
    }

    private async Task<ReferralRedemption?> RecordReferralRedemptionAsync(
        Studio? studio, bool hasDiscount, CancellationToken ct)
    {
        if (studio?.PendingReferralCodeId is not Guid refCodeId) return null;

        ReferralCode? code = await db.ReferralCodes.FirstOrDefaultAsync(r => r.Id == refCodeId, ct);

        ReferralRedemption newRedemption = new()
        {
            ReferralCodeId  = refCodeId,
            NewStudioId     = studio.Id,
            DiscountApplied = hasDiscount,
        };
        db.ReferralRedemptions.Add(newRedemption);

        if (code is { IsSingleUse: true } && hasDiscount)
            code.IsActive = false;

        studio.PendingReferralCodeId = null;

        return newRedemption;
    }
}

public class ActivateCheckoutSubscriptionValidator : AbstractValidator<ActivateCheckoutSubscriptionCommand>
{
    public ActivateCheckoutSubscriptionValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
