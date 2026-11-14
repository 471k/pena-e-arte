using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Common;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily sweep: expires any Notified waitlist entry whose claim window (WaitlistClaimWindow) has passed, then
/// offers the slot that entry was offered to the next Waiting entry in line, if the slot is still free. The
/// expiry query is cross-tenant (IgnoreQueryFilters, the same shape as every other daily platform-wide job,
/// see RetentionPurgeJob).
///
/// <para>The cascade runs per studio with that studio set as the tenant. ClaimNextMatchAsync reads waitlist
/// entries through the tenant filter, and with no tenant set (a Hangfire job has none) that filter matches
/// nothing, so before 2026-11-14 the cascade silently never found anyone in production. The unit tests didn't
/// show it because the fake context applies no query filters.</para>
///
/// <para>It cascades on the slot actually offered (NotifiedSlotStart / NotifiedArtistId /
/// NotifiedSlotDurationMinutes), not the expired client's own preferred window, and ClaimNextMatchAsync checks
/// that slot is still free first. An entry notified before those fields existed has no recorded slot: it is
/// expired but not cascaded, rather than guessing a slot.</para>
/// </summary>
public class WaitlistNotificationExpiryJob(
    IAppDbContext db,
    ICurrentTenant tenant,
    ISender sender,
    ILogger<WaitlistNotificationExpiryJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        DateTime cutoff = DateTime.UtcNow - WaitlistClaimWindow.Duration;

        List<Waitlist> expired = await db.WaitlistEntries
            .IgnoreQueryFilters()
            .Where(w => w.Status == WaitlistStatus.Notified && w.NotifiedAt != null && w.NotifiedAt < cutoff
                && w.DeletedAt == null)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        List<Guid> toNotify = [];
        int skippedNoSlot = 0;

        foreach (IGrouping<Guid, Waitlist> studioEntries in expired.GroupBy(w => w.StudioId))
        {
            tenant.SetTenant(studioEntries.Key);

            foreach (Waitlist entry in studioEntries)
            {
                entry.Status = WaitlistStatus.Expired;
                entry.UpdatedAt = DateTime.UtcNow;

                if (entry.NotifiedSlotStart is not DateTime slotStart || entry.NotifiedSlotDurationMinutes is not int duration)
                {
                    skippedNoSlot++;
                    logger.LogInformation(
                        "Waitlist entry {@WaitlistEntryId} expired with no recorded slot (notified before C09); not cascaded",
                        entry.Id);
                    continue;
                }

                Guid? matchedId = await db.ClaimNextMatchAsync(
                    entry.StudioId, entry.NotifiedArtistId, slotStart, duration, freedAppointmentId: null, ct);
                if (matchedId is Guid id) toNotify.Add(id);
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (Guid id in toNotify)
            await sender.Send(new SendWaitlistSlotAvailableNotificationCommand(id), ct);

        logger.LogInformation(
            "WaitlistNotificationExpiryJob expired {Expired} notified entries past their claim window, cascaded {Cascaded} to the next in line, {SkippedNoSlot} had no recorded slot",
            expired.Count, toNotify.Count, skippedNoSlot);
    }
}
