using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleSubscriptionUpdatedCommand(
    string StripeSubscriptionId,
    string StripeStatus,
    DateTime CurrentPeriodEnd,
    string? StripePriceId,
    bool CancelAtPeriodEnd = false) : IRequest;

public class HandleSubscriptionUpdatedHandler(IAppDbContext db) : IRequestHandler<HandleSubscriptionUpdatedCommand>
{
    public async Task Handle(HandleSubscriptionUpdatedCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        SubscriptionStatus previousStatus = subscription.Status;

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

        await db.SaveChangesAsync(ct);
    }
}
