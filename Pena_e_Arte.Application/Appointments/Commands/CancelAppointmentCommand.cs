using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record CancelAppointmentCommand(Guid AppointmentId) : IRequest;

public class CancelAppointmentHandler(
    IAppDbContext     db,
    ICurrentTenant    tenant,
    IRealtimeNotifier realtime,
    ISender           sender)
    : IRequestHandler<CancelAppointmentCommand>
{
    public async Task Handle(CancelAppointmentCommand command, CancellationToken ct)
    {
        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status == AppointmentStatus.Cancelled)
            return;

        if (appointment.Status == AppointmentStatus.Completed)
            throw new BusinessRuleViolationException("Completed appointments cannot be cancelled.");

        appointment.Status    = AppointmentStatus.Cancelled;
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentCancelled", new { command.AppointmentId }, ct);

        await sender.Send(new SendAppointmentCancellationCommand(appointment.Id), ct);
    }
}
