using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Two-stage data-retention job (GDPR Art. 5(1)(e) storage limitation + Art. 17 erasure).
/// Pass 1 (soft): marks consent forms past their retention window, and client profiles past the
/// body-map retention window, for deletion. Pass 2 (hard): permanently removes rows soft-deleted
/// longer than the grace window — consent forms (deleting their R2 file first) and client profiles
/// (health data: medical notes/allergies/DOB/body map) — and anonymizes the PII of any Client that
/// requested erasure past the grace window (the Client row itself can't be removed because
/// appointments/payments FK-reference it), deleting its Identity login too. Same cross-tenant,
/// IgnoreQueryFilters, two-pass shape as PaymentReconciliationJob — the soft-delete query filter
/// would otherwise hide exactly the rows this job must find.
/// </summary>
public class RetentionPurgeJob(
    IAppDbContext db,
    IR2Service r2,
    IIdentityService identity,
    IOptions<RetentionOptions> options,
    ILogger<RetentionPurgeJob> logger)
{
    private readonly RetentionOptions _opts = options.Value;

    public async Task RunAsync(CancellationToken ct = default)
    {
        await SoftPurgeExpiredAsync(ct);
        await HardPurgeConsentFormsAsync(ct);
        await HardPurgeClientProfilesAsync(ct);
        await AnonymizeErasedClientsAsync(ct);
    }

    // ── Pass 1: soft-delete rows past their retention window ──────────────────────

    private async Task SoftPurgeExpiredAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        // IgnoreQueryFilters: retention runs platform-wide across every tenant, and the
        // soft-delete filter (DeletedAt == null) would otherwise hide the rows to expire.
        // Same justified cross-tenant sweep precedent as PaymentReconciliationJob.
        DateTime consentCutoff = now.AddDays(-_opts.ConsentForms);
        List<ConsentForm> expiredForms = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && (c.SignedAt ?? c.CreatedAt) < consentCutoff)
            .ToListAsync(ct);

        foreach (ConsentForm form in expiredForms)
            form.DeletedAt = now;

        // Client profiles (body map + medical notes/allergies/DOB) are retained relative to the
        // client's LAST appointment (or the profile's creation, if they never booked). One grouped
        // query for the last-appointment date per client, then filter in memory — no N+1.
        DateTime bodyMapCutoff = now.AddDays(-_opts.BodyMaps);
        Dictionary<Guid, DateTime> lastApptByClient = await db.Appointments
            .IgnoreQueryFilters()
            .GroupBy(a => a.ClientId)
            .Select(g => new { ClientId = g.Key, Last = g.Max(a => a.Date) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Last, ct);

        List<ClientProfile> liveProfiles = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Where(p => p.DeletedAt == null)
            .ToListAsync(ct);

        int profilesExpired = 0;
        foreach (ClientProfile profile in liveProfiles)
        {
            DateTime lastActivity = lastApptByClient.TryGetValue(profile.ClientId, out DateTime last)
                ? last
                : profile.CreatedAt;
            if (lastActivity < bodyMapCutoff)
            {
                profile.DeletedAt = now;
                profilesExpired++;
            }
        }

        if (expiredForms.Count > 0 || profilesExpired > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "RetentionPurgeJob soft-deleted {Forms} consent forms and {Profiles} client profiles past their retention windows",
                expiredForms.Count, profilesExpired);
        }
    }

    // ── Pass 2a: hard-purge soft-deleted consent forms (delete R2 file first) ─────

    private async Task HardPurgeConsentFormsAsync(CancellationToken ct)
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

    // ── Pass 2b: hard-purge soft-deleted client profiles (the actual health data) ─

    private async Task HardPurgeClientProfilesAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-_opts.GracePeriodBeforeHardPurge);

        // BodyMap is a value object of plain location strings (no R2 images), so there is no
        // storage object to delete before removing the row — physically removing the ClientProfile
        // takes MedicalNotes/Allergies/DateOfBirth/BodyMap with it.
        List<ClientProfile> purgeable = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Where(p => p.DeletedAt != null && p.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (purgeable.Count == 0) return;

        db.ClientProfiles.RemoveRange(purgeable);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "RetentionPurgeJob hard-purged {Count} client profiles past the {Days}-day grace window",
            purgeable.Count, _opts.GracePeriodBeforeHardPurge);
    }

    // ── Pass 2c: anonymize the PII of erased Clients past the grace window ─────────

    private async Task AnonymizeErasedClientsAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-_opts.GracePeriodBeforeHardPurge);

        List<Client> toAnonymize = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => c.ErasureRequestedAt != null && c.ErasureRequestedAt < cutoff)
            .ToListAsync(ct);

        if (toAnonymize.Count == 0) return;

        // The Client row can't be removed (appointments/payments FK-reference it with RESTRICT), so
        // scrub its PII in place. Capture the Identity user id before nulling it, to delete the login.
        List<Guid> userIdsToDelete = [];
        foreach (Client client in toAnonymize)
        {
            if (client.UserId is Guid uid) userIdsToDelete.Add(uid);

            client.FirstName = "Deleted";
            client.LastName = "User";
            client.Email = $"deleted-{client.Id:N}@erased.invalid"; // unique, non-routable
            client.Phone = null;
            client.UserId = null;
            client.ErasureRequestedAt = null; // anonymization complete — don't reprocess next run
        }

        await db.SaveChangesAsync(ct);

        foreach (Guid uid in userIdsToDelete)
            await identity.DeleteUserAsync(uid, ct);

        logger.LogInformation(
            "RetentionPurgeJob anonymized {Count} erased client(s) past the {Days}-day grace window and deleted their logins",
            toAnonymize.Count, _opts.GracePeriodBeforeHardPurge);
    }
}
