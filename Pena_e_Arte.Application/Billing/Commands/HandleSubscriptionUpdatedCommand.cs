using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleSubscriptionUpdatedCommand(
    string StripeSubscriptionId,
    string StripeStatus,
    DateTime CurrentPeriodEnd,
    string? StripePriceId,
    long? UnitAmount,
    string? Currency,
    long? Quantity,
    decimal? RecurringDiscountPercent,
    bool CancelAtPeriodEnd = false,
    string? StripeEventId = null) : IRequest;

public class HandleSubscriptionUpdatedHandler(IAppDbContext db) : IRequestHandler<HandleSubscriptionUpdatedCommand>
{
    public async Task Handle(HandleSubscriptionUpdatedCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        SubscriptionStatus previousStatus = subscription.Status;
        decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);
        bool pendingWasSet = subscription.PendingPlanId is not null;

        subscription.Status = command.StripeStatus switch
        {
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "trialing" => SubscriptionStatus.Trialing,
            "canceled" => SubscriptionStatus.Cancelled,
            _ => subscription.Status
        };

        // PastDueReminderJob's day-1/3/7 escalation schedule is computed off this timestamp —
        // set it only on the transition INTO PastDue (not on every webhook while already
        // PastDue), and clear it the moment the subscription leaves PastDue for any reason.
        if (subscription.Status == SubscriptionStatus.PastDue && previousStatus != SubscriptionStatus.PastDue)
            subscription.PastDueSince = DateTime.UtcNow;
        else if (subscription.Status != SubscriptionStatus.PastDue)
            subscription.PastDueSince = null;

        subscription.CurrentPeriodEnd = command.CurrentPeriodEnd;
        subscription.CancelAtPeriodEnd = command.CancelAtPeriodEnd;

        // Trial is no longer applicable once the subscription is active on a paid plan.
        if (subscription.Status == SubscriptionStatus.Active)
            subscription.TrialExpiresAt = null;

        if (command.StripePriceId is not null)
        {
            PlanPrice? price = await db.PlanPrices
                .FirstOrDefaultAsync(pp => pp.StripePriceId == command.StripePriceId, ct);

            if (price is not null)
            {
                subscription.PlanId = price.PlanId;
                subscription.BillingInterval = price.Interval;

                // A scheduled change has landed — the pending change is no longer pending
                if (subscription.PendingPlanId == price.PlanId
                    && subscription.PendingBillingInterval == price.Interval)
                {
                    subscription.PendingPlanId = null;
                    subscription.PendingBillingInterval = null;
                }
            }
        }

        if (command.UnitAmount is long amount)
        {
            subscription.BilledUnitAmount = amount / 100m;
            subscription.BilledQuantity = command.Quantity is long q ? (int)q : 1;
            subscription.BilledCurrency = command.Currency;
        }
        subscription.RecurringDiscountPercent = command.RecurringDiscountPercent;

        // The PendingPlanId-clearing block above already tells us definitively whether THIS call
        // is the moment a scheduled change landed — it must not double-write against
        // ChangePlanHandler's own synchronous Expansion for an immediate upgrade (there
        // PendingPlanId is never set, so this stays false for the webhook echo).
        bool pendingLanded = pendingWasSet && subscription.PendingPlanId is null;
        decimal mrrAfter = MrrRules.MonthlyEquivalent(subscription);

        if (subscription.Status == SubscriptionStatus.PastDue && previousStatus != SubscriptionStatus.PastDue)
        {
            RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter, RevenueEventType.PastDue,
                nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
        }
        else if (previousStatus == SubscriptionStatus.PastDue && subscription.Status == SubscriptionStatus.Active)
        {
            RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter, RevenueEventType.Recovered,
                nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
        }
        else if (previousStatus is SubscriptionStatus.Active or SubscriptionStatus.PastDue
                 && subscription.Status == SubscriptionStatus.Cancelled)
        {
            // A status=canceled update that arrives before (or instead of) the deleted event.
            // Whichever webhook first flips the row to Cancelled records the churn; the other
            // then sees an already-Cancelled row and writes nothing.
            RevenueEventRecorder.Record(db, subscription, mrrBefore, 0m, RevenueEventType.Churn,
                nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
        }
        else if (pendingLanded && mrrBefore != mrrAfter)
        {
            // A landed scheduled change is always a downgrade in practice (ChangePlanHandler only
            // ever schedules downgrades — upgrades apply immediately), but compare amounts rather
            // than assume the direction, in case a list-price edit between scheduling and landing
            // flipped it.
            RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter,
                mrrAfter > mrrBefore ? RevenueEventType.Expansion : RevenueEventType.Contraction,
                nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
        }
        // Any OTHER active-status amount change (e.g. a bare Stripe Dashboard price edit with no
        // pending change and no status transition) deliberately writes NO event.

        await db.SaveChangesAsync(ct);
    }
}
