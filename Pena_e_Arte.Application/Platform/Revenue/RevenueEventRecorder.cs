using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform.Revenue;

/// <summary>
/// The one writer for the subscription revenue ledger — every MRR-affecting transition calls
/// this, so "compute before/after, skip if unchanged, build an idempotency key, insert" lives
/// in exactly one place. See architecture.md Decisions Log, "Subscription revenue ledger
/// (2026-09-24)".
/// </summary>
public static class RevenueEventRecorder
{
    private static readonly HashSet<RevenueEventType> AlwaysRecord =
        [RevenueEventType.PastDue, RevenueEventType.Recovered, RevenueEventType.Paused, RevenueEventType.Resumed];

    /// <summary>Records a movement unless it's a no-op (MrrBefore == MrrAfter) for a type
    /// whose whole point IS the amount changing (New/Expansion/Contraction/Churn/Reactivation).
    /// PastDue/Recovered/Paused/Resumed are state-only movements — always record them even
    /// when the contracted amount didn't change, since Type alone carries the meaning.</summary>
    public static void Record(
        IAppDbContext db, Subscription subscription, decimal mrrBefore, decimal mrrAfter,
        RevenueEventType type, string source, string? stripeEventId, DateTime? occurredAt = null)
    {
        if (mrrBefore == mrrAfter && !AlwaysRecord.Contains(type)) return;

        DateTime at = occurredAt ?? DateTime.UtcNow;

        db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = subscription.Id,
            StudioId = subscription.StudioId,
            OccurredAt = at,
            Type = type,
            PlanId = subscription.PlanId,
            Interval = subscription.BillingInterval,
            MrrBefore = mrrBefore,
            MrrAfter = mrrAfter,
            Source = source,
            // Webhook sources pass the real Stripe event id (idempotent across redelivery).
            // Non-webhook sources get a deterministic synthetic key so the unique index still
            // rejects an accidental double-call with the same occurredAt, without needing a
            // nullable-unindexed column.
            StripeEventId = stripeEventId ?? $"{source}:{subscription.Id}:{type}:{at:O}",
        });
    }

    /// <summary>The MRR a subscription contributes right before an activation: its contracted
    /// amount only when it was already billing (Active), otherwise 0 — a Cancelled/Trialing/
    /// GracePeriod subscription keeps its old plan and billed-amount fields but is not billing,
    /// so treating those as "before" would turn a reactivation into a fake contraction.</summary>
    public static decimal MrrBeforeActivation(Subscription subscription) =>
        subscription.Status == SubscriptionStatus.Active ? MrrRules.MonthlyEquivalent(subscription) : 0m;

    /// <summary>Records the ledger row for an activation (checkout, direct create, manual
    /// activation). A subscription that was already billing (mrrBefore above zero) becomes an
    /// Expansion/Contraction; otherwise it's New the first time the ledger sees this
    /// subscription and Reactivation every time after — the ledger itself is the source of
    /// truth for "ever billed", not Status, which can look identical for a fresh row and a
    /// churned-then-reactivating one at the moment of activation.</summary>
    public static async Task RecordActivationAsync(
        IAppDbContext db, Subscription subscription, decimal mrrBefore, decimal mrrAfter,
        string source, CancellationToken ct, bool knownNew = false)
    {
        RevenueEventType type;
        if (mrrBefore > 0m)
        {
            type = mrrAfter >= mrrBefore ? RevenueEventType.Expansion : RevenueEventType.Contraction;
        }
        else
        {
            bool everBilled = !knownNew
                && await db.SubscriptionRevenueEvents.AnyAsync(e => e.SubscriptionId == subscription.Id, ct);
            type = everBilled ? RevenueEventType.Reactivation : RevenueEventType.New;
        }

        Record(db, subscription, mrrBefore, mrrAfter, type, source, stripeEventId: null);
    }

    /// <summary>The Type of the most recent ledger row for a subscription (OccurredAt, then
    /// insertion order — same tiebreak as RevenueLedgerRules), or null when it has none. Used
    /// to keep state-only events (Paused/Resumed) from being written when they'd be
    /// meaningless or would resurrect a cancelled contract.</summary>
    public static Task<RevenueEventType?> LatestTypeAsync(
        IAppDbContext db, Guid subscriptionId, CancellationToken ct) =>
        db.SubscriptionRevenueEvents
            .Where(e => e.SubscriptionId == subscriptionId)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.CreatedAt)
            .Select(e => (RevenueEventType?)e.Type)
            .FirstOrDefaultAsync(ct);
}
