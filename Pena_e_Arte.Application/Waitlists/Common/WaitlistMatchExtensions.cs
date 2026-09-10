using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Waitlists.Common;

/// <summary>
/// Shared auto-FIFO waitlist match, called both when a slot frees up (CancelAppointmentHandler)
/// and when a notified entry's 24h claim window expires (WaitlistNotificationExpiryJob) so the
/// slot cascades to the next person in line rather than dead-ending after one missed claim.
/// Matches ONLY the first entry — never the whole matching set — so multiple guests don't race
/// for one slot.
/// </summary>
public static class WaitlistMatchExtensions
{
    /// <summary>
    /// Finds the oldest still-Waiting entry whose ArtistId (null or matching) and preferred-date
    /// window covers <paramref name="slotDate"/>, and flips it to Notified. Returns the matched
    /// entry's id (or null) so the caller can dispatch SendWaitlistSlotAvailableNotificationCommand
    /// at the right point in its own save/notify sequence — this only mutates the tracked entity,
    /// it never sends the notification or calls SaveChangesAsync itself.
    /// </summary>
    public static async Task<Guid?> ClaimNextMatchAsync(
        this IAppDbContext db,
        Guid studioId,
        Guid? artistId,
        DateTime slotDate,
        CancellationToken ct)
    {
        Domain.Entities.Waitlist? match = await db.WaitlistEntries
            .Where(w => w.StudioId == studioId
                && w.Status == WaitlistStatus.Waiting
                && (w.ArtistId == null || w.ArtistId == artistId)
                && w.PreferredDateFrom <= slotDate
                && w.PreferredDateTo >= slotDate)
            .OrderBy(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (match is null) return null;

        match.Status = WaitlistStatus.Notified;
        match.NotifiedAt = DateTime.UtcNow;
        match.UpdatedAt = DateTime.UtcNow;

        return match.Id;
    }
}
