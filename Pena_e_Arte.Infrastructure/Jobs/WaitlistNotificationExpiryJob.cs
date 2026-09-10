using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily sweep: expires any Notified waitlist entry whose 24h claim window has passed, then
/// re-runs the same auto-FIFO match against the still-Waiting entries for that slot/artist so the
/// slot cascades to the next person in line — the mechanism that makes "24h to claim" actually
/// expire rather than being a decorative timestamp. Cross-tenant, IgnoreQueryFilters — same shape
/// as every other daily platform-wide job (see RetentionPurgeJob).
/// </summary>
public class WaitlistNotificationExpiryJob(
    IAppDbContext db,
    ISender sender,
    ILogger<WaitlistNotificationExpiryJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-24);

        List<Waitlist> expired = await db.WaitlistEntries
            .IgnoreQueryFilters()
            .Where(w => w.Status == WaitlistStatus.Notified && w.NotifiedAt != null && w.NotifiedAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        List<Guid> toNotify = [];
        foreach (Waitlist entry in expired)
        {
            entry.Status = WaitlistStatus.Expired;
            entry.UpdatedAt = DateTime.UtcNow;

            // Re-run the FIFO match for this exact slot/artist so the next Waiting entry gets its
            // turn. Uses the entry's own PreferredDateFrom as the reference "slot" moment — the
            // original freed-slot Date is not retained on the Waitlist row itself, but any point
            // inside the notified entry's own preferred window still correctly matches every
            // other Waiting entry whose window also covers that same point in time.
            Guid? matchedId = await db.ClaimNextMatchAsync(
                entry.StudioId, entry.ArtistId, entry.PreferredDateFrom, ct);
            if (matchedId is Guid id) toNotify.Add(id);
        }

        await db.SaveChangesAsync(ct);

        foreach (Guid id in toNotify)
            await sender.Send(new SendWaitlistSlotAvailableNotificationCommand(id), ct);

        logger.LogInformation(
            "WaitlistNotificationExpiryJob expired {Expired} notified entries past their 24h claim window, cascaded {Cascaded} to the next in line",
            expired.Count, toNotify.Count);
    }
}
