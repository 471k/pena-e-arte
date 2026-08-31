using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Common;

public static class ArtistAvailabilityExtensions
{
    /// <summary>
    /// True if at least one active artist at <paramref name="studioId"/> has open schedule, no
    /// time-off, and no conflicting appointment covering [date, date + durationMinutes). Used
    /// only for the "book with the studio" path, where no specific artist has been chosen yet —
    /// this is a soft, advisory check. AssignAppointmentArtistCommand re-validates the specific
    /// artist actually chosen at assignment time, under a real per-artist lock, independent of
    /// this.
    /// Takes an explicit studioId AND calls IgnoreQueryFilters() on every query — approved
    /// exception, see architecture.md IgnoreQueryFilters() table — because this is called from
    /// both an authenticated context (CreateAppointmentCommand, RescheduleAppointmentCommand,
    /// CheckSlotAvailabilityQuery — ICurrentTenant.StudioId populated) and an anonymous one
    /// (CreateGuestAppointmentCommand via CreateAppointmentCoreAsync,
    /// CheckPublicSlotAvailabilityQuery — no JWT at all). For an anonymous caller,
    /// ICurrentTenant.StudioId defaults to Guid.Empty (CurrentTenantService), so EF Core's
    /// global query filter (StudioId == tenant.StudioId) would silently AND against every query
    /// below in addition to the explicit studioId predicate — always producing zero rows,
    /// regardless of the real studio — unless the ambient filter is explicitly bypassed here.
    /// The explicit studioId predicate makes IgnoreQueryFilters() safe for the authenticated
    /// caller too: results are identical either way since the predicate already scopes
    /// correctly. Found and fixed during the guest-checkout booking prompt, 2026-08-31 — every
    /// query in this file originally relied on the ambient filter alone and would have silently
    /// returned "unavailable"/empty for every guest request.
    /// </summary>
    public static async Task<bool> IsAnyArtistAvailableAsync(
        this IAppDbContext db, Guid studioId, DateTime date, int durationMinutes, CancellationToken ct)
    {
        DateTime end = date.AddMinutes(durationMinutes);
        DayOfWeek day = date.DayOfWeek;
        TimeSpan startTime = date.TimeOfDay;
        TimeSpan endTime = end.TimeOfDay;

        bool studioClosed = await db.StudioClosures.IgnoreQueryFilters().AnyAsync(
            c => c.StudioId == studioId && c.StartDate <= date.Date && c.EndDate >= date.Date, ct);
        if (studioClosed) return false;

        List<Guid> candidateArtistIds = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == studioId && a.IsActive && a.DeletedAt == null)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (Guid artistId in candidateArtistIds)
        {
            bool hasSchedule = await db.ArtistSchedules.IgnoreQueryFilters().AnyAsync(
                s => s.StudioId == studioId && s.ArtistId == artistId && s.DayOfWeek == day && s.IsAvailable
                     && startTime >= s.StartTime && endTime <= s.EndTime, ct);
            if (!hasSchedule) continue;

            bool onTimeOff = await db.ArtistTimeOffs.IgnoreQueryFilters().AnyAsync(
                t => t.StudioId == studioId && t.ArtistId == artistId
                     && t.StartDate <= date.Date && t.EndDate >= date.Date, ct);
            if (onTimeOff) continue;

            bool conflict = await db.Appointments.IgnoreQueryFilters().AnyAsync(
                a => a.StudioId == studioId && a.ArtistId == artistId && a.Date < end && a.EndDate > date
                     && a.Status != AppointmentStatus.Cancelled, ct);
            if (conflict) continue;

            return true; // this artist clears all three checks
        }

        return false;
    }

    /// <summary>
    /// Schedule-side specific-artist availability (studio-closure → schedule → hours →
    /// time-off — deliberately NO conflict check). Shared by CreateAppointmentCommand's
    /// CreateAppointmentCoreAsync (both authenticated and guest paths) for its PRE-LOCK check,
    /// and by <see cref="CheckArtistSlotAvailabilityAsync"/> below. Kept separate from the
    /// conflict check because CreateAppointmentCoreAsync must run its authoritative conflict
    /// check AFTER acquiring the per-slot lock (race safety — two concurrent requests could
    /// both pass a pre-lock conflict check) — that post-lock check is unchanged, still throws
    /// SlotAlreadyBookedException.
    /// IgnoreQueryFilters() + explicit studioId — see <see cref="IsAnyArtistAvailableAsync"/>'s
    /// doc comment for why both are required for the anonymous/guest callers.
    /// Note: prior to this extraction, CreateAppointmentHandler's inline specific-artist check
    /// did NOT check StudioClosures (only the any-artist path did) — that gap is closed here as
    /// a side effect of sharing this chain; see architecture.md's 2026-08-31 log entry.
    /// </summary>
    public static async Task<(bool Available, string? Reason)> CheckArtistScheduleAsync(
        this IAppDbContext db, Guid studioId, Guid artistId, DateTime date, int durationMinutes,
        CancellationToken ct)
    {
        DateTime end = date.AddMinutes(durationMinutes);
        DayOfWeek day = date.DayOfWeek;
        TimeSpan startTime = date.TimeOfDay;
        TimeSpan endTime = end.TimeOfDay;

        bool studioClosed = await db.StudioClosures.IgnoreQueryFilters().AnyAsync(
            c => c.StudioId == studioId && c.StartDate <= date.Date && c.EndDate >= date.Date, ct);
        if (studioClosed)
            return (false, "Studio is closed that day.");

        var schedule = await db.ArtistSchedules
            .IgnoreQueryFilters()
            .Where(s => s.StudioId == studioId && s.ArtistId == artistId &&
                        s.DayOfWeek == day && s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (schedule is null)
            return (false, $"Artist is not available on {day}s.");

        if (startTime < schedule.StartTime || endTime > schedule.EndTime)
            return (false, $"Outside artist's hours ({schedule.StartTime:hh\\:mm}–{schedule.EndTime:hh\\:mm}).");

        bool onLeave = await db.ArtistTimeOffs.IgnoreQueryFilters().AnyAsync(
            t => t.StudioId == studioId && t.ArtistId == artistId &&
                 t.StartDate <= date.Date && t.EndDate >= date.Date, ct);

        if (onLeave)
            return (false, "Artist is on leave that day.");

        return (true, null);
    }

    /// <summary>
    /// Full specific-artist availability chain (<see cref="CheckArtistScheduleAsync"/> +
    /// conflict check) — used by CheckSlotAvailabilityHandler and the public
    /// CheckPublicSlotAvailabilityQuery, where there is no subsequent lock/create step and the
    /// caller just wants a single yes/no/why preview. NOT used by CreateAppointmentCoreAsync's
    /// pre-lock check — see CheckArtistScheduleAsync's doc comment for why.
    /// </summary>
    public static async Task<(bool Available, string? Reason)> CheckArtistSlotAvailabilityAsync(
        this IAppDbContext db, Guid studioId, Guid artistId, DateTime date, int durationMinutes,
        CancellationToken ct)
    {
        (bool available, string? reason) = await db.CheckArtistScheduleAsync(studioId, artistId, date, durationMinutes, ct);
        if (!available) return (available, reason);

        DateTime end = date.AddMinutes(durationMinutes);
        bool conflict = await db.Appointments.IgnoreQueryFilters().AnyAsync(a =>
            a.StudioId == studioId &&
            a.ArtistId == artistId &&
            a.Date < end &&
            a.EndDate > date &&
            a.Status != AppointmentStatus.Cancelled, ct);

        if (conflict)
            return (false, "That slot is already booked.");

        return (true, null);
    }
}
