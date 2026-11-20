using MediatR;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// C03 (slice 7): the bookable starts for a duration over a range of studio-local days, for the signed-in booking
/// form and the reschedule dialog. A preview, not a reservation: the create and reschedule commands decide.
/// </summary>
/// <param name="ArtistId">Null means any artist, as for check-slot.</param>
/// <param name="ExcludeAppointmentId">The appointment being rescheduled, so its own slot isn't a conflict; only one
/// the caller may move (the reschedule path's own rule). Also marks the request as a reschedule, whose earliest start
/// is now rather than booking's 30-minute lead.</param>
public record GetAvailableSlotsQuery(
    Guid? ArtistId,
    DateOnly From,
    DateOnly To,
    int DurationMinutes,
    Guid? ExcludeAppointmentId = null)
    : IRequest<AvailableSlotsResult>;

public class GetAvailableSlotsHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<GetAvailableSlotsQuery, AvailableSlotsResult>
{
    public async Task<AvailableSlotsResult> Handle(GetAvailableSlotsQuery query, CancellationToken ct)
    {
        if (query.ExcludeAppointmentId is Guid excludeId)
            await db.EnsureCallerMayMoveAsync(currentUser, excludeId, ct);

        // The earliest start each command's own validator accepts: booking needs the 30-minute lead
        // (CreateAppointmentValidator), a reschedule only a future time (RescheduleAppointmentValidator).
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime earliest = query.ExcludeAppointmentId is null
            ? now.AddMinutes(BookingContentValidationRules.MinimumLeadMinutes)
            : now;

        return await db.FindAvailableSlotsAsync(
            tenant.StudioId, query.ArtistId, query.From, query.To, query.DurationMinutes,
            earliest, query.ExcludeAppointmentId, ct);
    }
}
