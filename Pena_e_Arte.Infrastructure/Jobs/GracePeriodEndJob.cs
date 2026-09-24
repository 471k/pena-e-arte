using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class GracePeriodEndJob(AppDbContext db)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == studioId, ct);

        if (subscription is null || subscription.Status != SubscriptionStatus.GracePeriod) return;

        decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);
        subscription.Status = SubscriptionStatus.Cancelled;

        // A subscription that never converted from trial was never billing — nothing to churn.
        bool hadPaid = await db.SubscriptionInvoicePayments.AnyAsync(p => p.SubscriptionId == subscription.Id, ct);
        if (hadPaid)
            RevenueEventRecorder.Record(db, subscription, mrrBefore, 0m, RevenueEventType.Churn,
                nameof(GracePeriodEndJob), stripeEventId: null);

        await db.SaveChangesAsync(ct);
    }
}
