using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Services;

// Evicts Redis subscription snapshots (sub:snapshot:{studioId}) whenever EF Core
// commits a change to any Subscription entity — including hard-deletes. This covers
// every application-layer mutation: webhooks, Hangfire jobs, and handler commands.
// Direct SQL mutations (e.g. emergency console ops) bypass this interceptor; in that
// case the 60 s TTL on SubscriptionAccessService is the safety net.
public class SubscriptionCacheInvalidationInterceptor(
    IDistributedCache cache,
    ILogger<SubscriptionCacheInvalidationInterceptor> logger) : SaveChangesInterceptor
{
    private List<Guid> _pendingInvalidations = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _pendingInvalidations = eventData.Context?.ChangeTracker
            .Entries<Subscription>()
            .Where(e => e.State is EntityState.Modified or EntityState.Added or EntityState.Deleted)
            .Select(e => e.Entity.StudioId)
            .Distinct()
            .ToList() ?? [];

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        foreach (Guid studioId in _pendingInvalidations)
        {
            try
            {
                await cache.RemoveAsync(SubscriptionAccessService.CacheKeyPrefix + studioId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Redis unavailable — DB commit already succeeded; TTL will expire the stale entry.
                logger.LogWarning(ex, "Failed to evict subscription cache for studio {StudioId}", studioId);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
