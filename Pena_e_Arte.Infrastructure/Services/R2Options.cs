namespace Pena_e_Arte.Infrastructure.Services;

public class R2Options
{
    public const string Section = "CloudflareR2";

    public string AccountId { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string PublicUrl { get; init; } = string.Empty;

    /// <summary>
    /// Destination bucket for R2ExportJob's daily backup copy (same Cloudflare account as
    /// BucketName, different bucket). Left unset until a dedicated backup bucket exists —
    /// R2ExportService degrades to a no-op when empty, same pattern as GeoIp:* being unset.
    /// See docs/infra/backup-dr-runbook.md.
    /// </summary>
    public string BackupBucketName { get; init; } = string.Empty;

    /// <summary>
    /// A SEPARATE R2 API token scoped only to BackupBucketName — deliberately not the same
    /// AccessKeyId/SecretAccessKey as the primary bucket above. R2's server-side CopyObject
    /// needs one credential with access to both source and destination bucket in the same
    /// request; rather than widen the app's live primary-bucket token (or grant it access to
    /// the backup bucket, expanding its blast radius), R2ExportService instead downloads via
    /// the primary credentials and uploads via these, so a misconfigured/compromised backup
    /// token can never touch the app's actual production storage path. See
    /// docs/infra/backup-dr-runbook.md.
    /// </summary>
    public string BackupAccessKeyId { get; init; } = string.Empty;
    public string BackupSecretAccessKey { get; init; } = string.Empty;
}
