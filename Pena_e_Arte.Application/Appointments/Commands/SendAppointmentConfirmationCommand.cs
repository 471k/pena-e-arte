using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record SendAppointmentConfirmationCommand(Guid AppointmentId) : IRequest<Unit>;

public class SendAppointmentConfirmationHandler(
    IAppDbContext                                      db,
    IEmailRenderer                                     emailRenderer,
    INotificationService                               notifications,
    ILogger<SendAppointmentConfirmationHandler>        logger)
    : IRequestHandler<SendAppointmentConfirmationCommand, Unit>
{
    public async Task<Unit> Handle(SendAppointmentConfirmationCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .Include(a => a.Client)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);

        if (appointment is null)
        {
            logger.LogWarning("Appointment {@AppointmentId} not found for confirmation email",
                command.AppointmentId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == appointment.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for confirmation email of appointment {@AppointmentId}",
                appointment.StudioId, appointment.Id);
            return Unit.Value;
        }

        string body = emailRenderer.RenderAppointmentConfirmation(
            appointment.Client.FirstName,
            appointment.Date,
            appointment.DurationMinutes,
            appointment.Notes,
            studio.ShowPlatformBranding);

        string subject = $"Appointment Confirmed — {appointment.Date:ddd, dd MMM yyyy 'at' HH:mm}";

        bool success = false;
        try
        {
            await notifications.SendEmailAsync(appointment.Client.Email, subject, body, ct);
            success = true;
            logger.LogInformation(
                "Confirmation email sent for appointment {@AppointmentId}",
                appointment.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send confirmation email for appointment {@AppointmentId}",
                appointment.Id);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            StudioId    = appointment.StudioId,
            RecipientId = appointment.ClientId,
            Channel     = NotificationChannel.Email,
            Subject     = subject,
            Body        = body,
            SentAt      = DateTime.UtcNow,
            IsSuccess   = success,
        });
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
