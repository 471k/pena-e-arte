using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Common;

public static class AppointmentMoveAccess
{
    /// <summary>
    /// Who may name an appointment as the one being moved (so its own slot doesn't count as a conflict): the same
    /// rule RescheduleAppointmentHandler applies before a move. A client only their own appointment (404, never 403,
    /// so a guessed id confirms nothing); staff any appointment in the studio (the tenant filter scopes the lookup).
    /// Moved here unchanged from CheckSlotAvailabilityHandler so the slot list (C03) applies the identical rule.
    /// </summary>
    public static async Task EnsureCallerMayMoveAsync(
        this IAppDbContext db, ICurrentUser currentUser, Guid appointmentId, CancellationToken ct)
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
