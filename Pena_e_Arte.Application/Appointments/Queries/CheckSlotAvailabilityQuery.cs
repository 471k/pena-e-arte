using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
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
            await EnsureCallerMayMoveAsync(excludeId, ct);

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

    // The same rule RescheduleAppointmentHandler applies before a move: a client only their own
    // appointment (404, never 403, so a guessed id confirms nothing); staff any appointment in the
    // studio (the tenant filter scopes the lookup).
    private async Task EnsureCallerMayMoveAsync(Guid appointmentId, CancellationToken ct)
    {
        Guid? clientId = await db.Appointments
            .Where(a => a.Id == appointmentId)
            .Select(a => (Guid?)a.ClientId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != clientId)
                throw new NotFoundException(nameof(Appointment), appointmentId);
        }
    }
}
