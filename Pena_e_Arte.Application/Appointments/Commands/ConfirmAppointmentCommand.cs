using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record ConfirmAppointmentCommand(Guid AppointmentId) : IRequest<AppointmentResponse>;

public class ConfirmAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IRealtimeNotifier realtime,
    ISender sender)
    : IRequestHandler<ConfirmAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(ConfirmAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status != AppointmentStatus.Pending)
            throw new BusinessRuleViolationException(
                $"Only Pending appointments can be confirmed (current: {appointment.Status}).");

        appointment.Status = AppointmentStatus.Confirmed;
        appointment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentConfirmed", response, ct);

        await sender.Send(new SendAppointmentConfirmationCommand(appointment.Id), ct);

        return response;
    }
}
