using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
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
    IAppDbContext db,
    ICurrentTenant tenant,
    IStripeBillingService billing,
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

        // D7 (2026-09-23): Yearly → Monthly is never an immediate "upgrade", whatever the
        // tier — Stripe's ProrationBehavior="always_invoice" would credit the unused part of
        // the discounted yearly price to the customer balance, bypassing the yearly refund
        // rule (used months charged at the monthly price). It always waits for the paid year
        // to end, same as any other downgrade.
        bool isUpgrade = MrrRules.MonthlyEquivalentOf(newPrice.Price, newPrice.Interval)
                > MrrRules.MonthlyEquivalentOf(currentPrice.Price, currentPrice.Interval)
            && !(currentPrice.Interval == BillingInterval.Yearly && newPrice.Interval == BillingInterval.Monthly);

        if (isUpgrade)
        {
            // Upgrade — switch now, charge the prorated difference immediately
            DateTime periodEnd = await billing.ChangeSubscriptionPriceAsync(
                subscription.StripeSubscriptionId, newPrice.StripePriceId, ct);

            // Falls back to the current plan's list price only for a pre-snapshot subscription
            // (Plan isn't loaded here, so MonthlyEquivalent alone would read 0).
            decimal mrrBefore = subscription.BilledUnitAmount is null
                ? MrrRules.MonthlyEquivalentOf(currentPrice.Price, currentPrice.Interval)
                : MrrRules.MonthlyEquivalent(subscription);

            subscription.PlanId = command.Request.PlanId;
            subscription.BillingInterval = requestedInterval;
            subscription.CurrentPeriodEnd = periodEnd;

            // The billed-amount snapshot is refreshed only by the following subscription.updated
            // webhook (deliberately a no-op for the ledger — see HandleSubscriptionUpdatedHandler),
            // so the new MRR is the new plan's list price under the subscription's existing
            // quantity and recurring discount. See architecture.md Decisions Log, "Subscription
            // revenue ledger (2026-09-24)" for the list-price-approximation flag.
            decimal discountFactor = 1m - (subscription.RecurringDiscountPercent ?? 0m) / 100m;
            decimal mrrAfter = MrrRules.MonthlyEquivalentOf(
                newPrice.Price * (subscription.BilledQuantity ?? 1) * discountFactor, newPrice.Interval);
            RevenueEventRecorder.Record(
                db, subscription, mrrBefore, mrrAfter,
                mrrAfter >= mrrBefore ? RevenueEventType.Expansion : RevenueEventType.Contraction,
                nameof(ChangePlanHandler), stripeEventId: null);

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

            subscription.PendingPlanId = command.Request.PlanId;
            subscription.PendingBillingInterval = requestedInterval;

            logger.LogInformation(
                "Plan downgrade scheduled at period end for studio {@StudioId} to plan {@PlanId} ({@Interval})",
                subscription.StudioId, command.Request.PlanId, requestedInterval);
        }

        await db.SaveChangesAsync(ct);
        return CreateSubscriptionHandler.Map(subscription);
    }
}
