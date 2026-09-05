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
}
