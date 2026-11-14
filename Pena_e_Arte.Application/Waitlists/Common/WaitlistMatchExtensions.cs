using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Waitlists.Common;

/// <summary>
/// Shared auto-FIFO waitlist match. Callers: CancelAppointmentHandler and RescheduleAppointmentHandler when a
/// slot frees up, WaitlistNotificationExpiryJob when a notified entry's claim window ends, and
/// WaitlistClaimCompletion when someone books from a claim at a different time than the slot they were
/// offered. Matches ONLY the first entry, never the whole matching set, so multiple guests don't race for
/// one slot.
///
/// <para><b>Contract (since 2026-11-14, C09): a cascade never notifies about a slot it hasn't just confirmed
/// is free.</b> Before, the match checked status, artist and date window only. Its first callers hid that,
/// because cancelling had just freed the slot; the expiry job (up to a day later) and a claim booked at
/// another time can't assume it. So the slot's availability is checked here, once, for every caller, with
/// the same schedule and conflict rules a booking uses.</para>
/// </summary>
public static class WaitlistMatchExtensions
{
    /// <summary>
    /// If the slot [<paramref name="slotStart"/>, + <paramref name="slotDurationMinutes"/>) is bookable, finds the
    /// oldest still-Waiting entry whose ArtistId (null or matching) and preferred-date window covers it, flips it
    /// to Notified and records which slot it was offered. Returns the matched entry's id (or null) so the caller
    /// can dispatch SendWaitlistSlotAvailableNotificationCommand at the right point in its own save/notify
    /// sequence. This only mutates the tracked entity; it never sends the notification or saves.
    /// <para>Reads waitlist entries through the tenant filter, so the caller must have the slot's studio as the
    /// current tenant (request handlers do; the expiry job sets it per studio).</para>
    /// </summary>
    /// <param name="freedAppointmentId">The appointment whose cancellation or move freed this slot, when the
    /// caller hasn't saved that change yet: its row still holds the slot in the database, so the availability
    /// check must not count it. Null when nothing unsaved is involved.</param>
    public static async Task<Guid?> ClaimNextMatchAsync(
        this IAppDbContext db,
        Guid studioId,
        Guid? artistId,
        DateTime slotStart,
        int slotDurationMinutes,
        Guid? freedAppointmentId,
        CancellationToken ct)
    {
        if (!await IsSlotFreeAsync(db, studioId, artistId, slotStart, slotDurationMinutes, freedAppointmentId, ct))
            return null;

        Domain.Entities.Waitlist? match = await db.WaitlistEntries
            .Where(w => w.StudioId == studioId
                && w.Status == WaitlistStatus.Waiting
                && (w.ArtistId == null || w.ArtistId == artistId)
                && w.PreferredDateFrom <= slotStart
                && w.PreferredDateTo >= slotStart)
            .OrderBy(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (match is null) return null;

        DateTime now = DateTime.UtcNow;
        match.Status = WaitlistStatus.Notified;
        // Whole seconds: the claim token binds to these ticks, and the column keeps fewer digits than .NET's
        // 100 ns ticks. Truncating here makes the tracked value, the saved value and every later read agree,
        // so a token minted from the still-tracked entity (the cancel path) matches the row tomorrow.
        match.NotifiedAt = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        match.NotifiedSlotStart = slotStart;
        match.NotifiedArtistId = artistId;
        match.NotifiedSlotDurationMinutes = slotDurationMinutes;
        match.UpdatedAt = now;

        return match.Id;
    }

    /// <summary>
    /// The slot is bookable now: for a specific artist, the same schedule-and-conflict check a booking runs; for
    /// a freed appointment with no artist, the studio's "any artist free" check. Studio-scoped explicitly, so it
    /// also works where no tenant is set.
    /// </summary>
    public static async Task<bool> IsSlotFreeAsync(
        IAppDbContext db,
        Guid studioId,
        Guid? artistId,
        DateTime slotStart,
        int slotDurationMinutes,
        Guid? freedAppointmentId,
        CancellationToken ct)
    {
        if (slotStart <= DateTime.UtcNow) return false;

        if (artistId is Guid artist)
        {
            (bool available, _) = await db.CheckArtistSlotAvailabilityAsync(
                studioId, artist, slotStart, slotDurationMinutes, ct, freedAppointmentId);
            return available;
        }

        // An appointment with no artist never blocks the any-artist check, so there's nothing to exclude.
        return await db.IsAnyArtistAvailableAsync(studioId, slotStart, slotDurationMinutes, ct);
    }
}
