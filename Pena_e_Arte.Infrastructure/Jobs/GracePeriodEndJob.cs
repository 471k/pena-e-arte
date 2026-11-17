using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Scheduled once per subscription entering grace (JobScheduler), not recurring: see TrialExpiryJob
/// for what the outcome line does and does not prove.
/// </summary>
public class GracePeriodEndJob(AppDbContext db, ILogger<GracePeriodEndJob> logger)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        string outcome = await EndAsync(studioId, ct);
        logger.LogInformation("GracePeriodEndJob {StudioId}: {Outcome}", studioId, outcome);
    }

    private async Task<string> EndAsync(Guid studioId, CancellationToken ct)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == studioId, ct);

        if (subscription is null) return "skipped, no subscription";
        if (subscription.Status != SubscriptionStatus.GracePeriod)
            return $"skipped, status is {subscription.Status}";

        decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);
        subscription.Status = SubscriptionStatus.Cancelled;

        // A subscription that never converted from trial was never billing — nothing to churn.
        bool hadPaid = await db.SubscriptionInvoicePayments.AnyAsync(p => p.SubscriptionId == subscription.Id, ct);
        if (hadPaid)
            RevenueEventRecorder.Record(db, subscription, mrrBefore, 0m, RevenueEventType.Churn,
                nameof(GracePeriodEndJob), stripeEventId: null);

        await db.SaveChangesAsync(ct);
        return hadPaid ? "grace ended, cancelled, churn recorded" : "grace ended, cancelled, never paid so no churn";
    }
}
