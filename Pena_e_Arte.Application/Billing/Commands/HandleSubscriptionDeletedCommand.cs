using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleSubscriptionDeletedCommand(string StripeSubscriptionId, string? StripeEventId = null) : IRequest;

public class HandleSubscriptionDeletedHandler(IAppDbContext db) : IRequestHandler<HandleSubscriptionDeletedCommand>
{
    public async Task Handle(HandleSubscriptionDeletedCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        SubscriptionStatus previousStatus = subscription.Status;
        decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelAtPeriodEnd = false;

        // Only a subscription that was actually billing churns. A row already Cancelled (admin/
        // owner cancel wrote the churn synchronously, or a redelivery) or one that never billed
        // (Trialing/GracePeriod) writes nothing — this is also what keeps the deleted-webhook echo
        // of an immediate cancellation from double-counting the churn.
        if (previousStatus is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
            RevenueEventRecorder.Record(db, subscription, mrrBefore, 0m, RevenueEventType.Churn,
                nameof(HandleSubscriptionDeletedHandler), command.StripeEventId);

        await db.SaveChangesAsync(ct);
    }
}
