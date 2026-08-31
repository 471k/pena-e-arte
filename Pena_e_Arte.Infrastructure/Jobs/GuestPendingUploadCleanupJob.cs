using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily sweep of R2 objects under "appointments/guest-pending/" — the anonymous presign
/// endpoint (GetPresignedGuestUploadUrlHandler) lets any visitor generate a write URL with zero
/// account cost, so an abandoned booking form leaves orphaned images with nothing else to clean
/// them up. Deletes anything older than 48h whose key is not referenced by any
/// AppointmentAttachment.ImageUrl. The pre-existing authenticated "appointments/pending/" prefix
/// has the identical latent gap (no cleanup job for it either) — deliberately NOT fixed here,
/// since that gap predates this feature and isn't made worse by it; only the new anonymous
/// prefix — new attack surface this prompt introduces — gets a job. See architecture.md's
/// 2026-08-31 log entry.
/// </summary>
public class GuestPendingUploadCleanupJob(
    IAppDbContext db,
    IR2Service r2,
    ILogger<GuestPendingUploadCleanupJob> logger)
{
    private const string Prefix = "appointments/guest-pending/";
    private static readonly TimeSpan MinAge = TimeSpan.FromHours(48);

    public async Task RunAsync(CancellationToken ct = default)
    {
        IReadOnlyList<R2ObjectInfo> objects = await r2.ListByPrefixAsync(Prefix, ct);
        if (objects.Count == 0) return;

        DateTime cutoff = DateTime.UtcNow - MinAge;
        List<R2ObjectInfo> candidates = objects.Where(o => o.LastModified < cutoff).ToList();
        if (candidates.Count == 0) return;

        // Cross-tenant, IgnoreQueryFilters: this Hangfire job runs with no request/tenant scope
        // at all, and a guest-pending upload isn't attached to any studio's tenant yet at the
        // time this check needs to run — same class as AppointmentReminderJob/etc. (approved
        // exception #36).
        HashSet<string> referencedUrls = (await db.AppointmentAttachments
            .IgnoreQueryFilters()
            .Select(a => a.ImageUrl)
            .ToListAsync(ct))
            .ToHashSet();

        int deleted = 0;
        foreach (R2ObjectInfo obj in candidates)
        {
            string publicUrl = r2.GetPublicUrl(obj.Key);
            if (referencedUrls.Contains(publicUrl)) continue;

            try
            {
                await r2.DeleteAsync(obj.Key, ct);
                deleted++;
            }
            catch (Exception ex)
            {
                // One storage failure must not block the rest of the sweep — no PII in this key,
                // safe to log in full; the object is retried on the next run.
                logger.LogWarning(ex,
                    "GuestPendingUploadCleanupJob could not delete orphaned object {@ObjectKey}; will retry next run",
                    obj.Key);
            }
        }

        logger.LogInformation(
            "GuestPendingUploadCleanupJob scanned {Scanned} objects, deleted {Deleted} orphaned guest-pending uploads",
            candidates.Count, deleted);
    }
}
