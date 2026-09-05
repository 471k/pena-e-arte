using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily backup copy (06:00 UTC, staggered after guest-pending-upload-cleanup at 05:00) of the
/// primary R2 bucket into a separate backup bucket — see IR2ExportService for why this exists
/// (R2 has no native object-versioning feature). No-ops gracefully via NullR2ExportService when
/// R2 isn't configured, or via R2ExportService's own internal guard when BackupBucketName isn't
/// set yet — either way this job never throws just because the backup bucket doesn't exist.
/// </summary>
public class R2ExportJob(IR2ExportService exportService, ILogger<R2ExportJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        R2ExportResult result = await exportService.RunAsync(ct);

        logger.LogInformation(
            "R2ExportJob completed: {@Copied} copied, {@Skipped} already up to date, {@Failed} failed",
            result.Copied, result.Skipped, result.Failed);
    }
}
