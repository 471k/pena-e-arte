using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record SendAppointmentCreatedNotificationCommand(Guid AppointmentId) : IRequest<Unit>;

public class SendAppointmentCreatedNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    ILogger<SendAppointmentCreatedNotificationHandler> logger)
    : IRequestHandler<SendAppointmentCreatedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendAppointmentCreatedNotificationCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .Include(a => a.Client)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);

        if (appointment is null)
        {
            logger.LogWarning("Appointment {@AppointmentId} not found for created notification",
                command.AppointmentId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == appointment.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for created notification of appointment {@AppointmentId}",
                appointment.StudioId, appointment.Id);
            return Unit.Value;
        }

        string clientFullName = $"{appointment.Client.FirstName} {appointment.Client.LastName}";
        string appointmentDate = appointment.Date.ToString(
            "dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.AppointmentCreated, NotificationChannel.Email, ct);
        bool smsEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.AppointmentCreated, NotificationChannel.Sms, ct);

        // Email to client
        string clientEmailBody = emailRenderer.RenderAppointmentCreatedClient(
            appointment.Client.FirstName,
            appointment.Date,
            appointment.DurationMinutes,
            studio.Name,
            studio.ShowPlatformBranding);

        string clientSubject = $"Booking request received — {studio.Name}";
        NotificationLog? clientLog = null;

        if (emailEnabled)
        {
            bool clientEmailSuccess = true;
            try
            {
                await notifications.SendEmailAsync(appointment.Client.Email, clientSubject, clientEmailBody, ct);
                logger.LogInformation(
                    "Appointment created client email sent for appointment {@AppointmentId}",
                    appointment.Id);
            }
            catch (Exception ex)
            {
                clientEmailSuccess = false;
                logger.LogWarning(ex,
                    "Failed to send appointment created client email for appointment {@AppointmentId}",
                    appointment.Id);
            }

            clientLog = new()
            {
                StudioId = studio.Id,
                RecipientId = appointment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel = NotificationChannel.Email,
                Subject = clientSubject,
                Body = clientEmailBody,
                SentAt = DateTime.UtcNow,
                IsSuccess = clientEmailSuccess,
            };
            db.NotificationLogs.Add(clientLog);

            // Email to studio owner
            string studioEmailBody = emailRenderer.RenderAppointmentCreatedStudio(
                clientFullName,
                appointment.Date,
                appointment.DurationMinutes,
                appointment.Notes);

            string studioSubject = $"New booking request from {clientFullName}";
            bool studioEmailSuccess = true;
            try
            {
                await notifications.SendEmailAsync(studio.OwnerEmail, studioSubject, studioEmailBody, ct);
                logger.LogInformation(
                    "Appointment created studio email sent for appointment {@AppointmentId}",
                    appointment.Id);
            }
            catch (Exception ex)
            {
                studioEmailSuccess = false;
                logger.LogWarning(ex,
                    "Failed to send appointment created studio email for appointment {@AppointmentId}",
                    appointment.Id);
            }

            db.NotificationLogs.Add(new NotificationLog
            {
                StudioId = studio.Id,
                RecipientId = studio.Id,
                RecipientType = NotificationRecipientType.Studio,
                Channel = NotificationChannel.Email,
                Subject = studioSubject,
                Body = studioEmailBody,
                SentAt = DateTime.UtcNow,
                IsSuccess = studioEmailSuccess,
            });

            await db.SaveChangesAsync(ct);
        }

        // SMS to client
        if (smsEnabled && appointment.Client.Phone is not null)
        {
            string smsBody =
                $"Hi {appointment.Client.FirstName}, your booking request at {studio.Name} " +
                $"on {appointmentDate} has been received and is pending confirmation.";

            bool smsSent = true;
            try
            {
                await notifications.SendSmsAsync(appointment.Client.Phone, smsBody, ct);
            }
            catch (Exception ex)
            {
                smsSent = false;
                logger.LogWarning(ex,
                    "Failed to send appointment created SMS for appointment {@AppointmentId}",
                    appointment.Id);
            }

            db.NotificationLogs.Add(new NotificationLog
            {
                StudioId = studio.Id,
                RecipientId = appointment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = smsBody,
                SentAt = DateTime.UtcNow,
                IsSuccess = smsSent,
            });
            await db.SaveChangesAsync(ct);
        }

        if (clientLog is not null)
        {
            await realtime.NotifyStudioAsync(
                studio.Id, "NotificationReceived",
                GetNotificationsHandler.Map(clientLog, clientFullName), ct);
        }

        return Unit.Value;
    }
}
