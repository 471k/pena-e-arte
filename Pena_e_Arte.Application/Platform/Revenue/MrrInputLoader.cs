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
/// </summary>
public static class MrrInputLoader
{
    public static async Task<List<SubscriptionRevenueInput>> LoadAsync(IAppDbContext db, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, AdminOnly. See architecture.md.
        List<Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
                    .ThenInclude(p => p!.Prices)
            .Where(s => s.Subscription != null)
            .ToListAsync(ct);

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
