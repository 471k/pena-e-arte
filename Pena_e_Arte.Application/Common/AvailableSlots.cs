using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Common;

/// <summary>One studio-local day of bookable starts, as UTC instants.</summary>
public sealed record AvailableSlotsDay(DateOnly Date, IReadOnlyList<DateTime> Starts);

/// <summary>
/// The bookable starts for a duration over a range of studio-local days, plus the first start at or after
/// the range's beginning within <see cref="AvailableSlots.NextAvailableHorizonDays"/> days.
/// </summary>
public sealed record AvailableSlotsResult(
    DateOnly From,
    DateOnly To,
    int DurationMinutes,
    int StepMinutes,
    IReadOnlyList<AvailableSlotsDay> Days,
    DateTime? NextAvailable);

/// <summary>
/// C03 (slice 7): the slot list. <b>A preview, not a reservation.</b> CreateAppointmentCoreAsync's claim-then-check
/// (ClaimArtistForBookingAsync, then the post-lock overlap check that throws SlotAlreadyBookedException) stays the
/// only authority; nothing here is binding, and a start offered here can be taken before it is submitted.
///
/// It decides with the same rule steps as the one-slot chain (<see cref="ArtistAvailabilityExtensions"/>'s
/// ToStudioWindow, StudioHoursRefusal, ArtistScheduleRefusal) and the same coverage and overlap comparisons, over
/// rows read once for the whole range instead of once per start. A specific artist mirrors
/// CheckArtistSlotAvailabilityAsync (no IsActive check, the excluded appointment never conflicts); "any artist"
/// mirrors IsAnyArtistAvailableAsync (active, not deleted artists; nothing excluded). The property tests compare
/// every start on the grid with those two functions.
///
/// Starts are on the studio's step (Studio.BookingSlotStepMinutes: 15, 30 or 60, Phi 20 Nov), or every session
/// length when it has none, from the later of the studio's and the artist's opening time. The step only spaces the
/// candidates; each one is still judged by the rule.
///
/// IgnoreQueryFilters() + explicit studioId on every read: the same approved usage and reason as
/// ArtistAvailabilityExtensions (registry row for ArtistAvailabilityExtensions in architecture.md) — called from the
/// anonymous public booking page, where the ambient tenant is Guid.Empty.
/// </summary>
public static class AvailableSlots
{
    /// <summary>The longest range one request may ask for, in days.</summary>
    public const int MaxRangeDays = 14;

    /// <summary>How far past the range's beginning the next-available answer looks.</summary>
    public const int NextAvailableHorizonDays = 60;

    public static async Task<AvailableSlotsResult> FindAvailableSlotsAsync(
        this IAppDbContext db,
        Guid studioId,
        Guid? artistId,
        DateOnly from,
        DateOnly to,
        int durationMinutes,
        DateTime earliestStartUtc,
        Guid? excludeAppointmentId,
        CancellationToken ct)
    {
        DateOnly horizonEnd = from.AddDays(NextAvailableHorizonDays - 1);
        if (horizonEnd < to) horizonEnd = to;

        string? timezone = await db.StudioTimezoneAsync(studioId, ct);
        // Studio has no tenant query filter (as CreateAppointmentCoreAsync relies on), so no bypass is needed here.
        int? configuredStep = await db.Studios
            .Where(s => s.Id == studioId)
            .Select(s => s.BookingSlotStepMinutes)
            .FirstOrDefaultAsync(ct);
        int stepMinutes = configuredStep ?? durationMinutes;
        DateTime localFrom = from.ToDateTime(TimeOnly.MinValue);
        DateTime localHorizonEnd = horizonEnd.ToDateTime(TimeOnly.MinValue);

        List<StudioClosure> closures = await db.StudioClosures.IgnoreQueryFilters()
            .Where(c => c.StudioId == studioId && c.DeletedAt == null
                        && c.StartDate <= localHorizonEnd && c.EndDate >= localFrom)
            .ToListAsync(ct);

        Dictionary<DayOfWeek, StudioHours> hoursByDay = (await db.StudioHours.IgnoreQueryFilters()
                .Where(h => h.StudioId == studioId && h.DeletedAt == null)
                .ToListAsync(ct))
            .GroupBy(h => h.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.First());

        // Specific artist: as CheckArtistSlotAvailabilityAsync, whose chain does not check IsActive.
        // Any artist: as IsAnyArtistAvailableAsync's candidate set.
        List<Guid> artistIds = artistId is Guid onlyArtist
            ? [onlyArtist]
            : await db.Artists.IgnoreQueryFilters()
                .Where(a => a.StudioId == studioId && a.IsActive && a.DeletedAt == null)
                .Select(a => a.Id)
                .ToListAsync(ct);

        if (artistIds.Count == 0)
            return Empty(from, to, durationMinutes, stepMinutes);

        Dictionary<(Guid ArtistId, DayOfWeek Day), ArtistSchedule> scheduleByArtistDay =
            (await db.ArtistSchedules.IgnoreQueryFilters()
                .Where(s => s.StudioId == studioId && s.DeletedAt == null
                            && artistIds.Contains(s.ArtistId) && s.IsAvailable)
                .ToListAsync(ct))
            .GroupBy(s => (s.ArtistId, s.DayOfWeek))
            .ToDictionary(g => g.Key, g => g.First());

        List<ArtistTimeOff> timeOffs = await db.ArtistTimeOffs.IgnoreQueryFilters()
            .Where(t => t.StudioId == studioId && t.DeletedAt == null && artistIds.Contains(t.ArtistId)
                        && t.StartDate <= localHorizonEnd && t.EndDate >= localFrom)
            .ToListAsync(ct);

        // Generous UTC margins around the local range: any zone offset is under a day, and no window is longer
        // than a day, so every appointment that can overlap a candidate is read.
        DateTime utcReadFrom = DateTime.SpecifyKind(localFrom.AddDays(-2), DateTimeKind.Utc);
        DateTime utcReadTo = DateTime.SpecifyKind(localHorizonEnd.AddDays(3), DateTimeKind.Utc);
        // The excluded appointment only applies to a specific artist, as in CheckArtistSlotAvailabilityAsync;
        // IsAnyArtistAvailableAsync excludes nothing (an unassigned appointment never conflicts with itself there).
        Guid? excluded = artistId is null ? null : excludeAppointmentId;
        List<BookedInterval> booked = await db.Appointments.IgnoreQueryFilters()
            .Where(a => a.StudioId == studioId && a.DeletedAt == null
                        && (excluded == null || a.Id != excluded)
                        && a.ArtistId != null && artistIds.Contains(a.ArtistId.Value)
                        && a.Date < utcReadTo && a.EndDate > utcReadFrom
                        && a.Status != AppointmentStatus.Cancelled)
            .Select(a => new BookedInterval(a.ArtistId!.Value, a.Date, a.EndDate))
            .ToListAsync(ct);

        Snapshot snapshot = new(timezone, closures, hoursByDay, artistIds, scheduleByArtistDay, timeOffs, booked);

        List<AvailableSlotsDay> days = [];
        DateTime? next = null;
        for (DateOnly day = from; day <= horizonEnd; day = day.AddDays(1))
        {
            bool inRange = day <= to;
            if (!inRange && next is not null) break;

            List<DateTime> starts = StartsOn(snapshot, day, durationMinutes, stepMinutes, earliestStartUtc);
            if (inRange) days.Add(new AvailableSlotsDay(day, starts));
            if (next is null && starts.Count > 0) next = starts[0];
        }

        return new AvailableSlotsResult(from, to, durationMinutes, stepMinutes, days, next);
    }

    private static AvailableSlotsResult Empty(DateOnly from, DateOnly to, int durationMinutes, int stepMinutes)
    {
        List<AvailableSlotsDay> days = [];
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
            days.Add(new AvailableSlotsDay(day, []));
        return new AvailableSlotsResult(from, to, durationMinutes, stepMinutes, days, null);
    }

    private static List<DateTime> StartsOn(
        Snapshot s, DateOnly day, int durationMinutes, int stepMinutes, DateTime earliestStartUtc)
    {
        if (!s.HoursByDay.TryGetValue(day.DayOfWeek, out StudioHours? hours) || !hours.IsOpen)
            return [];

        // Candidates only: each is then judged by the rule, so a candidate generated here can never make a start
        // bookable that the rule refuses.
        SortedSet<DateTime> candidates = [];
        DateTime localMidnight = day.ToDateTime(TimeOnly.MinValue);
        TimeSpan step = TimeSpan.FromMinutes(stepMinutes);
        foreach (Guid artist in s.ArtistIds)
        {
            if (!s.ScheduleByArtistDay.TryGetValue((artist, day.DayOfWeek), out ArtistSchedule? schedule))
                continue;
            TimeSpan anchor = hours.StartTime > schedule.StartTime ? hours.StartTime : schedule.StartTime;
            for (TimeSpan t = anchor; t < TimeSpan.FromDays(1); t += step)
            {
                DateTime utc = TimezoneUtils.StudioLocalToUtcForRules(localMidnight + t, s.Timezone);
                if (utc > earliestStartUtc) candidates.Add(utc);
            }
        }

        DateTime localDate = localMidnight.Date;
        List<DateTime> starts = [];
        foreach (DateTime utc in candidates)
        {
            ArtistAvailabilityExtensions.SlotWindow w = ArtistAvailabilityExtensions.ToStudioWindow(s.Timezone, utc, durationMinutes);
            // A start belongs to the day its own window falls on (a DST gap can move a candidate; never list it twice).
            if (w.LocalDate != localDate) continue;
            if (IsBookable(s, w)) starts.Add(w.UtcStart);
        }
        return starts;
    }

    // The chain's order and steps: closure, studio hours, then per artist schedule, time off, overlap.
    private static bool IsBookable(Snapshot s, ArtistAvailabilityExtensions.SlotWindow w)
    {
        if (s.Closures.Any(c => c.StartDate <= w.LocalDate && c.EndDate >= w.LocalDate))
            return false;

        s.HoursByDay.TryGetValue(w.Day, out StudioHours? hours);
        if (ArtistAvailabilityExtensions.StudioHoursRefusal(w, hours) is not null)
            return false;

        foreach (Guid artist in s.ArtistIds)
        {
            s.ScheduleByArtistDay.TryGetValue((artist, w.Day), out ArtistSchedule? schedule);
            if (ArtistAvailabilityExtensions.ArtistScheduleRefusal(w, schedule) is not null) continue;

            bool onLeave = s.TimeOffs.Any(t => t.ArtistId == artist
                                               && t.StartDate <= w.LocalDate && t.EndDate >= w.LocalDate);
            if (onLeave) continue;

            bool conflict = s.Booked.Any(b => b.ArtistId == artist && b.Start < w.UtcEnd && b.End > w.UtcStart);
            if (!conflict) return true;
        }
        return false;
    }

    public static AvailableSlotsResponse ToResponse(this AvailableSlotsResult result) => new(
        result.From,
        result.To,
        result.DurationMinutes,
        result.StepMinutes,
        result.Days.Select(d => new AvailableSlotsDayResponse(d.Date, d.Starts)).ToList(),
        result.NextAvailable);

    private sealed record BookedInterval(Guid ArtistId, DateTime Start, DateTime End);

    private sealed record Snapshot(
        string? Timezone,
        List<StudioClosure> Closures,
        Dictionary<DayOfWeek, StudioHours> HoursByDay,
        List<Guid> ArtistIds,
        Dictionary<(Guid ArtistId, DayOfWeek Day), ArtistSchedule> ScheduleByArtistDay,
        List<ArtistTimeOff> TimeOffs,
        List<BookedInterval> Booked);
}
