using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Two-stage data-retention job (GDPR Art. 5(1)(e) storage limitation + Art. 17 erasure):
/// pass 1 soft-deletes consent forms past the retention window; pass 2 hard-purges rows that
/// have been soft-deleted longer than the grace window (deleting their R2 file first). Same
/// cross-tenant, IgnoreQueryFilters, two-pass shape as PaymentReconciliationJob — the
/// soft-delete query filter would otherwise hide exactly the rows this job must find.
/// </summary>
public class RetentionPurgeJob(
    IAppDbContext db,
    IR2Service r2,
    IOptions<RetentionOptions> options,
    ILogger<RetentionPurgeJob> logger)
{
    private readonly RetentionOptions _opts = options.Value;

    public async Task RunAsync(CancellationToken ct = default)
    {
        await SoftPurgeExpiredAsync(ct);
        await HardPurgeGraceExpiredAsync(ct);
    }

    private async Task SoftPurgeExpiredAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-_opts.ConsentForms);

        // IgnoreQueryFilters: retention runs platform-wide across every tenant, and the
        // soft-delete filter (DeletedAt == null) would otherwise hide the rows to expire.
        // Same justified cross-tenant sweep precedent as PaymentReconciliationJob.
        List<ConsentForm> expired = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && (c.SignedAt ?? c.CreatedAt) < cutoff)
            .ToListAsync(ct);

        foreach (ConsentForm form in expired)
            form.DeletedAt = DateTime.UtcNow;

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "RetentionPurgeJob soft-deleted {Count} consent forms past the {Days}-day retention window",
                expired.Count, _opts.ConsentForms);
        }
    }

    private async Task HardPurgeGraceExpiredAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-_opts.GracePeriodBeforeHardPurge);

        List<ConsentForm> purgeable = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null && c.DeletedAt < cutoff)
            .ToListAsync(ct);

        int purged = 0;
        foreach (ConsentForm form in purgeable)
        {
            if (!string.IsNullOrEmpty(form.FileUrl))
            {
                try
                {
                    // Object key is deterministic from the form (see SignConsentFormHandler).
                    await r2.DeleteAsync($"consent/{form.StudioId}/{form.Id}.pdf", ct);
                }
                catch (Exception ex)
                {
                    // One storage failure must not block the whole run. Log the id only
                    // (never PII); the row stays and the next run retries it.
                    logger.LogWarning(ex,
                        "RetentionPurgeJob could not delete the R2 object for consent form {FormId}; will retry next run",
                        form.Id);
                    continue;
                }
            }

            db.ConsentForms.Remove(form);
            purged++;
        }

        if (purged > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "RetentionPurgeJob hard-purged {Count} consent forms past the {Days}-day grace window",
                purged, _opts.GracePeriodBeforeHardPurge);
        }
    }
}
