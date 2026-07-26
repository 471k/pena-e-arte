using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record CheckSlotAvailabilityQuery(
    Guid ArtistId,
    DateTime Date,
    int DurationMinutes)
    : IRequest<SlotAvailabilityResult>;

public record SlotAvailabilityResult(bool Available, string? Reason);

public class CheckSlotAvailabilityHandler(IAppDbContext db)
    : IRequestHandler<CheckSlotAvailabilityQuery, SlotAvailabilityResult>
{
    public async Task<SlotAvailabilityResult> Handle(
        CheckSlotAvailabilityQuery query, CancellationToken ct)
    {
        DateTime end = query.Date.AddMinutes(query.DurationMinutes);

        DayOfWeek day = query.Date.DayOfWeek;
        TimeSpan startTime = query.Date.TimeOfDay;
        TimeSpan endTime = end.TimeOfDay;

        bool studioClosed = await db.StudioClosures.AnyAsync(
            c => c.StartDate <= query.Date.Date &&
                 c.EndDate >= query.Date.Date, ct);

        if (studioClosed)
            return new SlotAvailabilityResult(false, "Studio is closed that day.");

        var schedule = await db.ArtistSchedules
            .Where(s => s.ArtistId == query.ArtistId &&
                        s.DayOfWeek == day &&
                        s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (schedule is null)
            return new SlotAvailabilityResult(false,
                $"Artist is not available on {day}s.");

        if (startTime < schedule.StartTime || endTime > schedule.EndTime)
            return new SlotAvailabilityResult(false,
                $"Outside artist's hours ({schedule.StartTime:hh\\:mm}–{schedule.EndTime:hh\\:mm}).");

        bool onLeave = await db.ArtistTimeOffs.AnyAsync(
            t => t.ArtistId == query.ArtistId &&
                 t.StartDate <= query.Date.Date &&
                 t.EndDate >= query.Date.Date, ct);

        if (onLeave)
            return new SlotAvailabilityResult(false, "Artist is on leave that day.");

        bool conflict = await db.Appointments.AnyAsync(a =>
            a.ArtistId == query.ArtistId &&
            a.Date < end &&
            a.EndDate > query.Date &&
            a.Status != AppointmentStatus.Cancelled, ct);

        if (conflict)
            return new SlotAvailabilityResult(false, "That slot is already booked.");

        return new SlotAvailabilityResult(true, null);
    }
}
