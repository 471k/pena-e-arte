using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily writer of Studio.StorageUsageBytes — the field PlanLimitService already reads for
/// QuotaType.StorageBytes checks but that, before this job, nothing anywhere ever wrote (it
/// permanently defaulted to 0). Completes P1 backlog item #19's storage dimension.
///
/// Why a daily sweep and not real-time: every upload in this codebase goes through
/// GetPresignedUploadUrlQuery/GetPresignedGuestUploadUrlQuery, which mint a presigned
/// direct-to-R2 PUT URL — the backend never observes the upload completing or its size, and
/// no entity stores a per-file SizeBytes. IR2Service.ListByPrefixAsync (already used by
/// GuestPendingUploadCleanupJob for per-prefix listing) is the only primitive that can
/// recover object sizes after the fact, so this job lists every object under each studio's
/// own "{studioId}/" prefix — the exact prefix GetPresignedUploadUrlHandler already scopes
/// upload keys to — and sums SizeBytes. Storage quota enforcement is therefore necessarily
/// eventual (up to ~24h stale), not synchronous; see architecture.md Decisions Log "Plan
/// Usage-Limit Enforcement completion (P1 #19)" for the accepted-tradeoff reasoning.
/// </summary>
public class StorageReconciliationJob(
    IAppDbContext db,
    IR2Service r2,
    ILogger<StorageReconciliationJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        List<Studio> studios = await db.Studios
            .Where(s => s.IsActive)
            .ToListAsync(ct);

        foreach (Studio studio in studios)
        {
            try
            {
                IReadOnlyList<R2ObjectInfo> objects = await r2.ListByPrefixAsync($"{studio.Id}/", ct);
                long totalBytes = objects.Sum(o => o.SizeBytes);

                studio.StorageUsageBytes = totalBytes;
            }
            catch (Exception ex)
            {
                // One studio's R2 listing failure must not block the rest of the sweep — its
                // StorageUsageBytes simply stays at yesterday's value until the next run.
                logger.LogWarning(ex,
                    "StorageReconciliationJob: failed to list objects for studio {@StudioId}", studio.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "StorageReconciliationJob reconciled storage usage for {StudioCount} studios", studios.Count);
    }
}
