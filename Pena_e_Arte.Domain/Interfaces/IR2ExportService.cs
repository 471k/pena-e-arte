namespace Pena_e_Arte.Domain.Interfaces;

public record R2ExportResult(int Copied, int Skipped, int Failed);

/// <summary>
/// One-way, copy-only mirror of the primary R2 bucket into a separate backup bucket — the
/// practical substitute for R2 having no native object-versioning feature (confirmed absent
/// 2026-09-05; see docs/infra/backup-dr-runbook.md). Never deletes from the backup bucket, even
/// when the source object is deleted — that would defeat the point of a backup by propagating
/// the exact accidental/malicious deletion it exists to recover from.
/// </summary>
public interface IR2ExportService
{
    Task<R2ExportResult> RunAsync(CancellationToken ct);
}
