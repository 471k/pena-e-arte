using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Deliberately uses two separate IAmazonS3 clients on two separate credentials — sourceS3
/// (the app's primary token, read-only here) and backupS3 (a dedicated token scoped only to
/// the backup bucket, injected via the "r2-backup" DI key) — rather than R2's server-side
/// CopyObject, which would need one credential with access to both buckets. See R2Options.
/// </summary>
public class R2ExportService(
    IAmazonS3 sourceS3,
    [FromKeyedServices("r2-backup")] IAmazonS3 backupS3,
    IOptions<R2Options> options,
    ILogger<R2ExportService> logger) : IR2ExportService
{
    private readonly R2Options _opts = options.Value;

    public async Task<R2ExportResult> RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BackupBucketName))
        {
            logger.LogWarning(
                "CloudflareR2:BackupBucketName not configured — skipping R2 export run. See docs/infra/backup-dr-runbook.md.");
            return new R2ExportResult(0, 0, 0);
        }

        int copied = 0, skipped = 0, failed = 0;
        string? continuationToken = null;

        do
        {
            ListObjectsV2Response page = await sourceS3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _opts.BucketName,
                ContinuationToken = continuationToken,
            }, ct);

            foreach (S3Object obj in page.S3Objects ?? [])
            {
                try
                {
                    if (await NeedsCopyAsync(obj, ct))
                    {
                        await DownloadThenUploadAsync(obj, ct);
                        copied++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    // One object's copy failure must not abort the run — same reasoning as
                    // GuestPendingUploadCleanupJob's per-object try/catch. Retried next run
                    // since NeedsCopyAsync will still see it as missing/stale.
                    failed++;
                    logger.LogWarning(ex, "R2ExportService failed to copy object {@ObjectKey}; will retry next run", obj.Key);
                }
            }

            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (continuationToken is not null);

        // Per-object failures are swallowed above so one bad object never aborts the run — but
        // that means a systemic problem (backup bucket deleted, API token lost access) would
        // otherwise degrade to "every object fails, every night, forever" with nothing but
        // WARNING-level logs nobody watches. Throwing here when literally everything failed (as
        // opposed to occasional flakiness) surfaces it as a real Hangfire Failed job, which
        // HangfireJobFailureLogFilter logs and the alerting-runbook's Hangfire rule can page on.
        if (failed > 0 && copied == 0 && skipped == 0)
        {
            throw new InvalidOperationException(
                $"R2ExportService: all {failed} object(s) failed to copy — likely a systemic issue (backup bucket missing, credentials lost access), not per-object flakiness. See warning logs above for the underlying exceptions.");
        }

        return new R2ExportResult(copied, skipped, failed);
    }

    private async Task<bool> NeedsCopyAsync(S3Object sourceObject, CancellationToken ct)
    {
        try
        {
            GetObjectMetadataResponse dest = await backupS3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _opts.BackupBucketName,
                Key = sourceObject.Key,
            }, ct);

            // ETag differing means the source object was overwritten since the last backup —
            // re-copy so the backup reflects the latest content, not just the latest key list.
            return dest.ETag != sourceObject.ETag;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }
    }

    private async Task DownloadThenUploadAsync(S3Object sourceObject, CancellationToken ct)
    {
        using GetObjectResponse getResponse = await sourceS3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _opts.BucketName,
            Key = sourceObject.Key,
        }, ct);

        using MemoryStream buffer = new();
        await getResponse.ResponseStream.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        await backupS3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _opts.BackupBucketName,
            Key = sourceObject.Key,
            InputStream = buffer,
            ContentType = getResponse.Headers.ContentType,
        }, ct);
    }
}
