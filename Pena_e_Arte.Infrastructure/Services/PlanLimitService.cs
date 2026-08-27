using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Resolves the current tenant's active Plan and compares live usage against its
/// Max* fields. Usage counts are cached briefly (mirrors SubscriptionAccessService's
/// pattern) since this runs on every quota-checked command, not just reads. Depends on
/// IAppDbContext (not the concrete AppDbContext) so it can be unit tested with
/// FakeDbContext rather than requiring an integration test.
/// </summary>
public class PlanLimitService(
    IAppDbContext db,
    ICurrentTenant tenant,
    IDistributedCache cache,
    ILogger<PlanLimitService> logger) : IPlanLimitService
{
    private const string CacheKeyPrefix = "plan:usage:";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
    };

    public Task EnsureWithinLimitAsync(QuotaType quotaType, CancellationToken ct = default) =>
        EnsureWithinLimitAsync(tenant.StudioId, quotaType, ct);

    public async Task EnsureWithinLimitAsync(Guid studioId, QuotaType quotaType, CancellationToken ct = default)
    {
        Plan? plan = await ResolveCurrentPlanAsync(studioId, ct);

        // No resolvable plan (e.g. mid-trial before a plan is chosen) — nothing to enforce.
        if (plan is null) return;

        int? max = quotaType switch
        {
            QuotaType.Artists => plan.MaxArtists,
            QuotaType.AppointmentsPerMonth => plan.MaxAppointmentsPerMonth,
            QuotaType.NotificationsPerMonth => plan.MaxNotificationsPerMonth,
            QuotaType.StorageBytes => plan.MaxStorageGb,
            QuotaType.Locations => plan.MaxLocations,
            _ => null
        };

        if (max is null) return; // unlimited on this plan

        long current = await GetCurrentUsageAsync(studioId, quotaType, ct);
        long limit = quotaType == QuotaType.StorageBytes
            ? (long)max.Value * 1024L * 1024L * 1024L
            : max.Value;

        if (current >= limit)
            throw new PlanLimitExceededException(
                $"This studio's plan allows up to {max.Value} {Describe(quotaType)}. Upgrade the plan to continue.");
    }

    public async Task<PlanUsageSnapshot?> GetUsageSnapshotAsync(CancellationToken ct = default)
    {
        Plan? plan = await ResolveCurrentPlanAsync(tenant.StudioId, ct);
        if (plan is null) return null;

        double artists = await GetCurrentUsageAsync(tenant.StudioId, QuotaType.Artists, ct);
        double appointments = await GetCurrentUsageAsync(tenant.StudioId, QuotaType.AppointmentsPerMonth, ct);
        double notifications = await GetCurrentUsageAsync(tenant.StudioId, QuotaType.NotificationsPerMonth, ct);
        double storageBytes = await GetCurrentUsageAsync(tenant.StudioId, QuotaType.StorageBytes, ct);
        double locations = await GetCurrentUsageAsync(tenant.StudioId, QuotaType.Locations, ct);

        double storageGb = Math.Round(storageBytes / 1024.0 / 1024.0 / 1024.0, 1);

        return new PlanUsageSnapshot(
            plan.Name,
            new PlanUsageDimension(artists, plan.MaxArtists),
            new PlanUsageDimension(appointments, plan.MaxAppointmentsPerMonth),
            new PlanUsageDimension(notifications, plan.MaxNotificationsPerMonth),
            new PlanUsageDimension(storageGb, plan.MaxStorageGb),
            new PlanUsageDimension(locations, plan.MaxLocations));
    }

    // Write-through invalidation: called by quota-checked command handlers immediately after
    // their write succeeds, so the very next EnsureWithinLimitAsync call for this dimension
    // reflects reality instead of waiting up to the 30s cache TTL. This narrows the staleness
    // window from "up to 30s" to "the time between two back-to-back sequential requests," but
    // does NOT eliminate the race for two truly concurrent requests that both read the cache
    // before either write completes — that needs a DB-level atomic counter or advisory lock,
    // which is out of scope here (see docs/claude/architecture.md Decisions Log).
    public async Task InvalidateUsageCacheAsync(QuotaType quotaType, CancellationToken ct = default)
    {
        string key = CacheKey(tenant.StudioId, quotaType);

        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Redis unavailable — plan usage cache invalidation skipped for studio {StudioId}", tenant.StudioId);
        }
    }

    private async Task<Plan?> ResolveCurrentPlanAsync(Guid studioId, CancellationToken ct)
    {
        Subscription? subscription = await db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StudioId == studioId, ct);

        return subscription?.Plan;
    }

    private static string CacheKey(Guid studioId, QuotaType quotaType) => $"{CacheKeyPrefix}{studioId}:{quotaType}";

    // Explicitly scoped to studioId (IgnoreQueryFilters + explicit StudioId comparisons) rather
    // than relying on the ambient ICurrentTenant-driven global query filter — this method is also
    // called with a studio that may differ from the current tenant (see the explicit-studioId
    // overload of EnsureWithinLimitAsync above); using the global filter here would silently
    // count the wrong studio's usage in that case.
    private async Task<long> GetCurrentUsageAsync(Guid studioId, QuotaType quotaType, CancellationToken ct)
    {
        string key = CacheKey(studioId, quotaType);

        try
        {
            byte[]? cached = await cache.GetAsync(key, ct);
            if (cached is not null && cached.Length == 8) return BitConverter.ToInt64(cached);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Redis unavailable — plan usage cache read skipped for studio {StudioId}", studioId);
        }

        DateTime monthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        long usage = quotaType switch
        {
            QuotaType.Artists => await db.Artists.IgnoreQueryFilters()
                .Where(a => a.StudioId == studioId && a.DeletedAt == null)
                .CountAsync(ct),

            QuotaType.AppointmentsPerMonth => await db.Appointments.IgnoreQueryFilters()
                .Where(a => a.StudioId == studioId && a.DeletedAt == null && a.CreatedAt >= monthStart)
                .CountAsync(ct),

            QuotaType.NotificationsPerMonth => await db.NotificationLogs.IgnoreQueryFilters()
                .Where(n => n.StudioId == studioId && n.DeletedAt == null && n.CreatedAt >= monthStart)
                .CountAsync(ct),

            QuotaType.StorageBytes => await db.Studios
                .Where(s => s.Id == studioId)
                .Select(s => s.StorageUsageBytes)
                .FirstOrDefaultAsync(ct),

            // Multi-location isn't modeled yet (Studio is still a single-location entity)
            // — always report 1 so this dimension never blocks until that feature ships.
            QuotaType.Locations => 1,

            _ => 0
        };

        try
        {
            await cache.SetAsync(key, BitConverter.GetBytes(usage), CacheOptions, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Redis unavailable — plan usage cache write skipped for studio {StudioId}", studioId);
        }

        return usage;
    }

    private static string Describe(QuotaType quotaType) => quotaType switch
    {
        QuotaType.Artists => "artists",
        QuotaType.AppointmentsPerMonth => "appointments per month",
        QuotaType.NotificationsPerMonth => "notifications per month",
        QuotaType.StorageBytes => "GB of storage",
        QuotaType.Locations => "locations",
        _ => "items"
    };
}
