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

public record ChangePlanCommand(ChangePlanRequest Request) : IRequest<SubscriptionResponse>;

/// <summary>
/// Switches an active Stripe-billed subscription to another plan, another billing
/// interval, or both. Upgrades (higher monthly-equivalent price) apply immediately
/// with proration; downgrades are scheduled for the end of the current billing period.
/// </summary>
public class ChangePlanHandler(
    IAppDbContext              db,
    ICurrentTenant             tenant,
    IStripeBillingService      billing,
    ILogger<ChangePlanHandler> logger)
    : IRequestHandler<ChangePlanCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(ChangePlanCommand command, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        if (subscription.Status != SubscriptionStatus.Active)
            throw new BusinessRuleViolationException(
                "Plan changes require an active subscription. Use the subscribe flow instead.");
        if (subscription.StripeSubscriptionId is null)
            throw new BusinessRuleViolationException(
                "This subscription is billed outside Stripe. Contact the platform to change plans.");
        if (subscription.PendingPlanId is not null)
            throw new BusinessRuleViolationException(
                "A plan change is already scheduled. Cancel it before choosing another plan.");

        BillingInterval requestedInterval =
            Enum.Parse<BillingInterval>(command.Request.BillingInterval, ignoreCase: true);

        if (subscription.PlanId == command.Request.PlanId && subscription.BillingInterval == requestedInterval)
            throw new BusinessRuleViolationException("The studio is already on this plan.");

        PlanPrice newPrice = await db.PlanPrices
            .FirstOrDefaultAsync(pp =>
                pp.PlanId == command.Request.PlanId && pp.Interval == requestedInterval && pp.IsActive, ct)
            ?? throw new BusinessRuleViolationException(
                "The selected plan is not available at that billing interval. Contact the platform.");

        PlanPrice currentPrice = await db.PlanPrices
            .FirstOrDefaultAsync(pp => pp.PlanId == subscription.PlanId && pp.Interval == subscription.BillingInterval, ct)
            ?? throw new BusinessRuleViolationException(
                "The current plan's pricing could not be determined. Contact the platform.");

        if (newPrice.StripePriceId is null || currentPrice.StripePriceId is null)
            throw new BusinessRuleViolationException(
                "The selected plan is not available for online billing. Contact the platform.");

        if (MonthlyEquivalent(newPrice) > MonthlyEquivalent(currentPrice))
        {
            // Upgrade — switch now, charge the prorated difference immediately
            DateTime periodEnd = await billing.ChangeSubscriptionPriceAsync(
                subscription.StripeSubscriptionId, newPrice.StripePriceId, ct);

            subscription.PlanId           = command.Request.PlanId;
            subscription.BillingInterval  = requestedInterval;
            subscription.CurrentPeriodEnd = periodEnd;

            logger.LogInformation(
                "Plan upgraded immediately for studio {@StudioId} to plan {@PlanId} ({@Interval})",
                subscription.StudioId, command.Request.PlanId, requestedInterval);
        }
        else
        {
            // Downgrade — the studio keeps what it paid for; switch at period end
            string newPriceInterval = requestedInterval == BillingInterval.Monthly ? "month" : "year";
            await billing.ScheduleSubscriptionPriceChangeAsync(
                subscription.StripeSubscriptionId, currentPrice.StripePriceId!, newPrice.StripePriceId, newPriceInterval, ct);

            subscription.PendingPlanId           = command.Request.PlanId;
            subscription.PendingBillingInterval  = requestedInterval;

            logger.LogInformation(
                "Plan downgrade scheduled at period end for studio {@StudioId} to plan {@PlanId} ({@Interval})",
                subscription.StudioId, command.Request.PlanId, requestedInterval);
        }

        await db.SaveChangesAsync(ct);
        return CreateSubscriptionHandler.Map(subscription);
    }

    // Normalise to a per-month cost so monthly and yearly plans compare fairly
    private static decimal MonthlyEquivalent(PlanPrice price) =>
        price.Interval == BillingInterval.Monthly ? price.Price : price.Price / 12m;
}
