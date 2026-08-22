using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Common;

public static class ArtistAvailabilityExtensions
{
    /// <summary>
    /// True if at least one active artist at the (query-filter-scoped) studio has open
    /// schedule, no time-off, and no conflicting appointment covering
    /// [date, date + durationMinutes). Used only for the "book with the studio" path, where
    /// no specific artist has been chosen yet — this is a soft, advisory check.
    /// AssignAppointmentArtistCommand re-validates the specific artist actually chosen at
    /// assignment time, under a real per-artist lock, independent of this.
    /// No explicit studioId parameter — db.Artists/ArtistSchedules/ArtistTimeOffs/Appointments
    /// are already tenant-scoped by EF Core's global query filters, exactly as
    /// CreateAppointmentCommand's existing single-artist checks rely on today.
    /// </summary>
    public static async Task<bool> IsAnyArtistAvailableAsync(
        this IAppDbContext db, DateTime date, int durationMinutes, CancellationToken ct)
    {
        DateTime end = date.AddMinutes(durationMinutes);
        DayOfWeek day = date.DayOfWeek;
        TimeSpan startTime = date.TimeOfDay;
        TimeSpan endTime = end.TimeOfDay;

        bool studioClosed = await db.StudioClosures.AnyAsync(
            c => c.StartDate <= date.Date && c.EndDate >= date.Date, ct);
        if (studioClosed) return false;

        List<Guid> candidateArtistIds = await db.Artists
            .Where(a => a.IsActive)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (Guid artistId in candidateArtistIds)
        {
            bool hasSchedule = await db.ArtistSchedules.AnyAsync(
                s => s.ArtistId == artistId && s.DayOfWeek == day && s.IsAvailable
                     && startTime >= s.StartTime && endTime <= s.EndTime, ct);
            if (!hasSchedule) continue;

            bool onTimeOff = await db.ArtistTimeOffs.AnyAsync(
                t => t.ArtistId == artistId && t.StartDate <= date.Date && t.EndDate >= date.Date, ct);
            if (onTimeOff) continue;

            bool conflict = await db.Appointments.AnyAsync(
                a => a.ArtistId == artistId && a.Date < end && a.EndDate > date
                     && a.Status != AppointmentStatus.Cancelled, ct);
            if (conflict) continue;

            return true; // this artist clears all three checks
        }

        return false;
    }
}
