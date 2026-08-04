using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily rollup (02:30 UTC, staggered from the existing 02:00 payment-reconciliation and 03:00
/// Instagram-sync jobs): aggregates yesterday's TrafficEvent rows into TrafficDailyAggregate,
/// then purges TrafficEvent rows older than 35 days. Neither table carries a query filter, so
/// no IgnoreQueryFilters() is needed — same "no filter to bypass" reasoning as the read queries
/// (see decision §2.2 / architecture.md's approved-usages table, entry #36's Hangfire-job class).
/// Idempotent on re-run: always recomputes and overwrites counts for the target date rather than
/// insert-only, so running it twice for the same day produces the same result, not double-counts.
/// </summary>
public class TrafficRollupJob(IAppDbContext db, ILogger<TrafficRollupJob> logger)
{
    private const int RawRetentionDays = 35;

    public async Task RunAsync(CancellationToken ct = default)
    {
        DateOnly targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        int rowsAggregated = await AggregateDayAsync(targetDate, ct);
        int rowsPurged = await PurgeOldEventsAsync(ct);

        logger.LogInformation(
            "TrafficRollupJob completed for {@TargetDate}: {@RowsAggregated} buckets aggregated, {@RowsPurged} raw rows purged",
            targetDate, rowsAggregated, rowsPurged);
    }

    private async Task<int> AggregateDayAsync(DateOnly targetDate, CancellationToken ct)
    {
        DateTime dayStart = targetDate.ToDateTime(TimeOnly.MinValue);
        DateTime dayEnd = dayStart.AddDays(1);

        // Aggregated server-side (GROUP BY/COUNT/COUNT DISTINCT translated to SQL) — no raw
        // TrafficEvent row set (with its Path/geo/UA columns) is ever pulled into memory here.
        var buckets = await db.TrafficEvents
            .Where(t => t.CreatedAt >= dayStart && t.CreatedAt < dayEnd)
            .GroupBy(t => new { t.StudioId, t.Role, t.CountryCode })
            .Select(g => new
            {
                g.Key.StudioId,
                g.Key.Role,
                g.Key.CountryCode,
                VisitCount = g.Count(),
                UniqueVisitorCount = g.Select(t => t.VisitorId).Distinct().Count(),
            })
            .ToListAsync(ct);

        if (buckets.Count == 0) return 0;

        Dictionary<(Guid? StudioId, string? Role, string? CountryCode), TrafficDailyAggregate> existing =
            await db.TrafficDailyAggregates
                .Where(a => a.Date == targetDate)
                .ToDictionaryAsync(a => (a.StudioId, a.Role, a.CountryCode), ct);

        foreach (var bucket in buckets)
        {
            if (existing.TryGetValue((bucket.StudioId, bucket.Role, bucket.CountryCode), out TrafficDailyAggregate? row))
            {
                row.UpdateCounts(bucket.VisitCount, bucket.UniqueVisitorCount);
            }
            else
            {
                db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(
                    targetDate, bucket.StudioId, bucket.Role, bucket.CountryCode,
                    bucket.VisitCount, bucket.UniqueVisitorCount));
            }
        }

        await db.SaveChangesAsync(ct);
        return buckets.Count;
    }

    private async Task<int> PurgeOldEventsAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-RawRetentionDays);

        List<TrafficEvent> stale = await db.TrafficEvents
            .Where(t => t.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        db.TrafficEvents.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
