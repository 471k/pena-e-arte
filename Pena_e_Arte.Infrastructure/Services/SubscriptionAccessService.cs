using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pena_e_Arte.Infrastructure.Services;

public class SubscriptionAccessService(
    AppDbContext      db,
    IDistributedCache cache,
    ILogger<SubscriptionAccessService> logger) : ISubscriptionAccessService
{
    public const string CacheKeyPrefix = "sub:snapshot:";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Not cached — suspension must take effect immediately on every request.
    public async Task<bool> IsStudioActiveAsync(Guid studioId, CancellationToken ct = default) =>
        await db.Studios
            .AsNoTracking()
            .Where(s => s.Id == studioId)
            .Select(s => s.IsActive)
            .FirstOrDefaultAsync(ct);

    public async Task<SubscriptionSnapshot?> GetSnapshotAsync(Guid studioId, CancellationToken ct = default)
    {
        string key = CacheKeyPrefix + studioId;

        try
        {
            byte[]? cached = await cache.GetAsync(key, ct);
            if (cached is not null)
                return JsonSerializer.Deserialize<SubscriptionSnapshot>(cached, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis unavailable — subscription cache read skipped for studio {StudioId}", studioId);
        }

        SubscriptionSnapshot? snapshot = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.StudioId == studioId)
            .Select(s => new SubscriptionSnapshot(s.Status, s.TrialExpiresAt, s.GracePeriodEnd))
            .FirstOrDefaultAsync(ct);

        // Null (missing row = data corruption) is never cached so the 402 fires on every
        // request rather than waiting for a TTL to expire.
        //
        // Only pass-through states are cached (Active, Trialing, GracePeriod). Blocking
        // states (PastDue, Cancelled) are not cached so that a payment or admin fix
        // takes effect on the very next request, even when done via direct SQL.
        // EF transitions out of pass-through states are still covered by the
        // SubscriptionCacheInvalidationInterceptor (Active/Trialing → PastDue etc.).
        if (snapshot is not null && IsPassThrough(snapshot.Status))
        {
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
                await cache.SetAsync(key, bytes, CacheOptions, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis unavailable — subscription cache write skipped for studio {StudioId}", studioId);
            }
        }

        return snapshot;
    }

    public async Task InvalidateCacheAsync(Guid studioId, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(CacheKeyPrefix + studioId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis unavailable — subscription cache invalidation skipped for studio {StudioId}", studioId);
        }
    }

    private static bool IsPassThrough(SubscriptionStatus status) =>
        status is SubscriptionStatus.Active
               or SubscriptionStatus.Trialing
               or SubscriptionStatus.GracePeriod;
}
