using FluentValidation;
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

public record CreateSubscriptionCheckoutCommand(CreateCheckoutRequest Request)
    : IRequest<CheckoutSessionResponse>;

/// <summary>
/// Starts a Stripe Checkout session so the owner can enter card details and pay for a
/// subscription. The studio is NOT activated here — activation happens once Stripe
/// confirms payment (webhook checkout.session.completed, mirrored by the finalize
/// endpoint on return). A pending referral coupon is attached to the session if valid.
/// </summary>
public class CreateSubscriptionCheckoutHandler(
    IAppDbContext                              db,
    ICurrentTenant                             tenant,
    IStripeBillingService                      billing,
    IStripeDiscountService                     discounts,
    ILogger<CreateSubscriptionCheckoutHandler> logger)
    : IRequestHandler<CreateSubscriptionCheckoutCommand, CheckoutSessionResponse>
{
    public async Task<CheckoutSessionResponse> Handle(CreateSubscriptionCheckoutCommand command, CancellationToken ct)
    {
        CreateCheckoutRequest req = command.Request;

        Domain.Entities.Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == req.PlanId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Plan), req.PlanId);

        Subscription subscription = await db.Subscriptions
            .Include(s => s.Studio)
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        // Card-billed studios change plans via Stripe (proration); a cash-billed Active
        // studio is allowed here so its owner can switch to card billing.
        if (subscription.Status == SubscriptionStatus.Active && subscription.StripeSubscriptionId is not null)
            throw new BusinessRuleViolationException(
                "Studio already has a card subscription. Use change plan instead.");

        BillingInterval requestedInterval =
            Enum.Parse<BillingInterval>(req.BillingInterval, ignoreCase: true);

        PlanPrice? price = await db.PlanPrices
            .FirstOrDefaultAsync(pp => pp.PlanId == plan.Id && pp.Interval == requestedInterval && pp.IsActive, ct);

        string? priceId = price?.StripePriceId;

        if (priceId is null || subscription.Studio is null)
            throw new BusinessRuleViolationException(
                "This plan is not available for online checkout. Please contact the platform.");

        if (subscription.Studio.StripeCustomerId is null)
        {
            subscription.Studio.StripeCustomerId =
                await billing.CreateCustomerAsync(subscription.Studio.OwnerEmail, ct);
            await db.SaveChangesAsync(ct);
        }

        string? couponId = await ResolveReferralCouponAsync(subscription.Studio, ct);

        // Cash-billed studios already paid through CurrentPeriodEnd — start the card
        // subscription as a trial until then so the first card charge falls on that date
        // (no double charge for a period they already covered in cash).
        DateTime? trialEnd =
            subscription is { Status: SubscriptionStatus.Active, StripeSubscriptionId: null }
            && subscription.CurrentPeriodEnd > DateTime.UtcNow
                ? subscription.CurrentPeriodEnd
                : null;

        string url = await billing.CreateSubscriptionCheckoutAsync(
            subscription.Studio.StripeCustomerId!,
            priceId,
            tenant.StudioId.ToString(),
            req.SuccessUrl,
            req.CancelUrl,
            couponId,
            trialEnd,
            ct);

        return new CheckoutSessionResponse(url);
    }

    private async Task<string?> ResolveReferralCouponAsync(Studio studio, CancellationToken ct)
    {
        if (studio.PendingReferralCodeId is not Guid refCodeId) return null;

        ReferralCode? code = await db.ReferralCodes
            .FirstOrDefaultAsync(r => r.Id == refCodeId && r.IsActive, ct);
        if (code is null || (code.ExpiresAt is not null && code.ExpiresAt < DateTime.UtcNow))
            return null;

        try
        {
            string couponId = await discounts.CreateOneMonthFreeCouponAsync(
                $"referral-coupon-{studio.Id}", ct);
            logger.LogInformation(
                "Attaching referral coupon to checkout for studio {@StudioId}", studio.Id);
            return couponId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Referral coupon creation failed for studio {@StudioId}; continuing without discount", studio.Id);
            return null;
        }
    }
}

public class CreateSubscriptionCheckoutValidator : AbstractValidator<CreateSubscriptionCheckoutCommand>
{
    public CreateSubscriptionCheckoutValidator()
    {
        RuleFor(x => x.Request.PlanId).NotEmpty();
        RuleFor(x => x.Request.BillingInterval)
            .NotEmpty()
            .Must(v => Enum.TryParse<BillingInterval>(v, ignoreCase: true, out _))
            .WithMessage("BillingInterval must be 'Monthly' or 'Yearly'.");
        RuleFor(x => x.Request.SuccessUrl).NotEmpty().Must(BeAbsoluteHttpUrl)
            .WithMessage("SuccessUrl must be an absolute http(s) URL.");
        RuleFor(x => x.Request.CancelUrl).NotEmpty().Must(BeAbsoluteHttpUrl)
            .WithMessage("CancelUrl must be an absolute http(s) URL.");
    }

    // Prefix check, not Uri.TryCreate — Stripe success URLs carry the literal
    // {CHECKOUT_SESSION_ID} placeholder, which is not a valid URI character.
    private static bool BeAbsoluteHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
