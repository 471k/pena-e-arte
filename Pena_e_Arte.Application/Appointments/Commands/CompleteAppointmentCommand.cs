using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record CompleteAppointmentCommand(Guid AppointmentId) : IRequest<AppointmentResponse>;

public class CompleteAppointmentHandler(
    IAppDbContext     db,
    ICurrentTenant    tenant,
    IRealtimeNotifier realtime)
    : IRequestHandler<CompleteAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(CompleteAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new BusinessRuleViolationException(
                $"Cannot complete an appointment with status {appointment.Status}.");

        appointment.Status    = AppointmentStatus.Completed;
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentCompleted", response, ct);

        return response;
    }
}
