using MediatR;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <param name="ExcludeAppointmentId">E04b (A02): the appointment being rescheduled, so its own current
/// slot doesn't count as a conflict, as the save path already excludes it. Optional; null for every
/// booking caller. Only an appointment the caller may move can be excluded, by the reschedule path's
/// own ownership rule: a client their own appointment (anything else is 404), staff any in the studio.</param>
public record CheckSlotAvailabilityQuery(
    Guid? ArtistId,
    DateTime Date,
    int DurationMinutes,
    Guid? ExcludeAppointmentId = null)
    : IRequest<SlotAvailabilityResult>;

public record SlotAvailabilityResult(bool Available, string? Reason);

public class CheckSlotAvailabilityHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<CheckSlotAvailabilityQuery, SlotAvailabilityResult>
{
    public async Task<SlotAvailabilityResult> Handle(
        CheckSlotAvailabilityQuery query, CancellationToken ct)
    {
        if (query.ExcludeAppointmentId is Guid excludeId)
            await db.EnsureCallerMayMoveAsync(currentUser, excludeId, ct);

        // No artist: the any-artist check only counts appointments that have an artist, so an
        // unassigned appointment being moved never conflicts with itself there.
        if (query.ArtistId is null)
        {
            bool anyAvailable = await db.IsAnyArtistAvailableAsync(tenant.StudioId, query.Date, query.DurationMinutes, ct);
            return anyAvailable
                ? new SlotAvailabilityResult(true, null)
                : new SlotAvailabilityResult(false, "No artist is available at that time.");
        }

        (bool available, string? reason) = await db.CheckArtistSlotAvailabilityAsync(
            tenant.StudioId, query.ArtistId.Value, query.Date, query.DurationMinutes, ct, query.ExcludeAppointmentId);
        return new SlotAvailabilityResult(available, reason);
    }
}
