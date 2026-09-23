using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Revenue;

/// <summary>
/// Loads every studio with a subscription plus the extra facts MrrRules needs, for
/// GetPlatformStatsHandler and GetMrrHistoryHandler — the two admin revenue figures now
/// share one read as well as one set of rules. See architecture.md Decisions Log,
/// "One MRR definition (2026-09-23)".
///
/// Known limitation: <c>adminCancelledAt</c> is the latest SubscriptionCancelledByAdmin audit
/// timestamp ever recorded for the studio, with no lower bound. Subscription is one row
/// reused across a studio's whole lifetime (CreatedAt never resets on resubscribe), so a
/// studio that was admin-cancelled once, resubscribed, and is later cancelled again by any
/// other means (Stripe deletes it, grace period expires) would have that first cancellation's
/// timestamp applied to the second cycle's reconstructed history — only ever under-counting
/// (D3's stated direction), and only reachable once a studio has actually gone through two
/// full billing cycles, which none has as of this writing. A correct fix needs a real
/// per-cycle boundary, which the Batch 2b/3 per-invoice ledger will provide — not worth a
/// schema change here for a history-chart-only edge case.
/// </summary>
public static class MrrInputLoader
{
    /// <summary>
    /// <paramref name="preloadedStudios"/> lets a caller that already loaded every studio with
    /// the same Subscription/Plan/Prices Include chain (e.g. GetPlatformStatsHandler, which
    /// needs the full list for its own status counts too) skip a second, near-duplicate
    /// studios query — pass null to have this method load them itself (GetMrrHistoryHandler,
    /// which has no studios list of its own).
    /// </summary>
    public static async Task<List<SubscriptionRevenueInput>> LoadAsync(
        IAppDbContext db, CancellationToken ct, IEnumerable<Studio>? preloadedStudios = null)
    {
        List<Studio> studios;
        if (preloadedStudios is not null)
        {
            studios = preloadedStudios.Where(s => s.Subscription is not null).ToList();
        }
        else
        {
            // IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, AdminOnly. See architecture.md.
            studios = await db.Studios
                .IgnoreQueryFilters()
                .Include(s => s.Subscription)
                    .ThenInclude(sub => sub!.Plan)
                        .ThenInclude(p => p!.Prices)
                .Where(s => s.Subscription != null)
                .ToListAsync(ct);
        }

        Dictionary<Guid, DateTime> adminCancelledAt = await db.AuditLogEntries
            .Where(a => a.Action == AuditActions.SubscriptionCancelledByAdmin
                        && a.TargetType == AuditTargetTypes.Subscription)
            .GroupBy(a => a.TargetId)
            .Select(g => new { StudioId = g.Key, At = g.Max(a => a.CreatedAt) })
            .ToDictionaryAsync(x => x.StudioId, x => x.At, ct);

        Dictionary<Guid, DateTime> suspendedAt = await db.AuditLogEntries
            .Where(a => a.Action == AuditActions.StudioSuspended && a.TargetType == AuditTargetTypes.Studio)
            .GroupBy(a => a.TargetId)
            .Select(g => new { StudioId = g.Key, At = g.Max(a => a.CreatedAt) })
            .ToDictionaryAsync(x => x.StudioId, x => x.At, ct);

        List<SubscriptionRevenueInput> inputs = new(studios.Count);
        foreach (Studio studio in studios)
        {
            DateTime? adminCancelled = adminCancelledAt.TryGetValue(studio.Id, out DateTime cancelledAt)
                ? cancelledAt
                : null;
            DateTime? suspended = !studio.IsActive && suspendedAt.TryGetValue(studio.Id, out DateTime suspAt)
                ? suspAt
                : null;

            inputs.Add(new SubscriptionRevenueInput(
                studio.Subscription!,
                studio.TrialExpiresAt,
                adminCancelled,
                studio.IsActive,
                suspended));
        }

        return inputs;
    }
}
