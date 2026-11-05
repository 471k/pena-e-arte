using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record SendAftercareNotificationCommand(Guid AppointmentId) : IRequest;

public class SendAftercareNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    ILogger<SendAftercareNotificationHandler> logger)
    : IRequestHandler<SendAftercareNotificationCommand>
{
    public async Task Handle(SendAftercareNotificationCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .Include(a => a.Client)
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);

        if (appointment is null)
        {
            logger.LogWarning("Appointment {@AppointmentId} not found for aftercare notification",
                command.AppointmentId);
            return;
        }

        if (appointment.AftercareSentAt.HasValue)
            return;

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == appointment.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning("Studio {@StudioId} not found for aftercare notification", appointment.StudioId);
            return;
        }

        if (appointment.Artist is null)
        {
            logger.LogWarning("Appointment {@AppointmentId} has no assigned artist for aftercare notification",
                appointment.Id);
            return;
        }

        string artistName = $"{appointment.Artist.FirstName} {appointment.Artist.LastName}";
        string body = emailRenderer.RenderAftercare(
            appointment.Client.FirstName, studio.Name, artistName, studio.ShowPlatformBranding);

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.Aftercare, NotificationChannel.Email, ct);

        if (emailEnabled)
        {
            string subject = $"Tattoo aftercare instructions — {studio.Name}";
            bool emailSent = true;
            try
            {
                await notifications.SendEmailAsync(appointment.Client.Email, subject, body, ct);
            }
            catch (Exception ex)
            {
                emailSent = false;
                logger.LogWarning(ex,
                    "Failed to send aftercare email for appointment {@AppointmentId}", appointment.Id);
            }
            RecordAttempt(appointment, NotificationChannel.Email, subject, body, emailSent);
        }

        string smsBody =
            $"Hi {appointment.Client.FirstName}, thanks for your session at {studio.Name}! " +
            "Keep it covered 2–4h, wash gently, moisturise twice daily, avoid sun/water for 2 weeks.";

        bool smsEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.Aftercare, NotificationChannel.Sms, ct);

        if (smsEnabled && appointment.Client.Phone is not null)
        {
            bool smsSent = true;
            try
            {
                await notifications.SendSmsAsync(appointment.Client.Phone, smsBody, ct);
            }
            catch (Exception ex)
            {
                smsSent = false;
                logger.LogWarning(ex,
                    "Failed to send aftercare SMS for appointment {@AppointmentId}", appointment.Id);
            }
            RecordAttempt(appointment, NotificationChannel.Sms, "Aftercare instructions", smsBody, smsSent);
        }

        // AftercareSentAt still means "the aftercare step ran" (it is set even if a channel failed or
        // both were off), exactly as before. Whether each message actually went out is the log rows above.
        appointment.AftercareSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // E04b (A08): one row per attempted channel, linked to the appointment, the way the other client
    // notifications already log theirs, so the appointment can show "sent" or "failed" honestly.
    // IsSuccess means the provider accepted the message; nothing records delivery or reading.
    private void RecordAttempt(Appointment appointment, NotificationChannel channel, string subject, string body, bool success) =>
        db.NotificationLogs.Add(new NotificationLog
        {
            StudioId = appointment.StudioId,
            RecipientId = appointment.ClientId,
            RecipientType = NotificationRecipientType.Client,
            Channel = channel,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
            AppointmentId = appointment.Id,
        });
}
