using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class AppointmentReminderJob(
    INotificationService             notifications,
    AppDbContext                     db,
    IRealtimeNotifier                realtime,
    ILogger<AppointmentReminderJob>  logger)
{
    public async Task SendReminderAsync(Guid appointmentId, string type, CancellationToken ct = default)
    {
        Appointment? appointment = await db.Appointments
            .IgnoreQueryFilters()
            .Include(a => a.Client)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DeletedAt == null, ct);

        if (appointment is null)
        {
            logger.LogWarning("Appointment {@AppointmentId} not found for reminder job", appointmentId);
            return;
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            logger.LogInformation("Skipping reminder for cancelled appointment {@AppointmentId}", appointmentId);
            return;
        }

        string timeLabel = type == "48h" ? "48 hours" : "24 hours";
        string subject   = $"Appointment Reminder — {appointment.Date:ddd, dd MMM yyyy 'at' HH:mm}";
        string emailBody = BuildEmailBody(appointment, timeLabel);

        bool emailSuccess = await TrySendEmailAsync(appointment, subject, emailBody, ct);
        NotificationLog emailLog = BuildLog(appointment, NotificationChannel.Email, subject, emailBody, emailSuccess);
        db.NotificationLogs.Add(emailLog);

        List<NotificationLog> logs = [emailLog];

        if (appointment.Client.Phone is not null)
        {
            string smsBody    = $"Reminder: Your tattoo session is in {timeLabel} — {appointment.Date:ddd dd MMM 'at' HH:mm}. Reply STOP to opt out.";
            bool   smsSuccess = await TrySendSmsAsync(appointment, smsBody, ct);
            NotificationLog smsLog = BuildLog(appointment, NotificationChannel.Sms, null, smsBody, smsSuccess);
            db.NotificationLogs.Add(smsLog);
            logs.Add(smsLog);
        }

        await db.SaveChangesAsync(ct);

        foreach (NotificationLog log in logs)
            await realtime.NotifyStudioAsync(
                appointment.StudioId, "NotificationReceived", ToResponse(log), ct);
    }

    private static NotificationLogResponse ToResponse(NotificationLog log) => new(
        log.Id, log.RecipientId, log.Channel.ToString(),
        log.Subject, log.Body, log.SentAt, log.IsSuccess, log.CreatedAt);

    private async Task<bool> TrySendEmailAsync(Appointment appointment, string subject, string body, CancellationToken ct)
    {
        try
        {
            await notifications.SendEmailAsync(appointment.Client.Email, subject, body, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send reminder email for appointment {@AppointmentId}", appointment.Id);
            return false;
        }
    }

    private async Task<bool> TrySendSmsAsync(Appointment appointment, string body, CancellationToken ct)
    {
        try
        {
            await notifications.SendSmsAsync(appointment.Client.Phone!, body, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send reminder SMS for appointment {@AppointmentId}", appointment.Id);
            return false;
        }
    }

    private static NotificationLog BuildLog(
        Appointment         appointment,
        NotificationChannel channel,
        string?             subject,
        string              body,
        bool                success) => new()
    {
        StudioId    = appointment.StudioId,
        RecipientId = appointment.ClientId,
        Channel     = channel,
        Subject     = subject,
        Body        = body,
        SentAt      = DateTime.UtcNow,
        IsSuccess   = success
    };

    private static string BuildEmailBody(Appointment appointment, string timeLabel) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2 style="color:#1a1a1a">Your appointment is in {timeLabel}</h2>
          <p>Hi {WebUtility.HtmlEncode(appointment.Client.FirstName)},</p>
          <p>This is a reminder that your tattoo appointment is scheduled for:</p>
          <p style="font-size:1.1em;font-weight:bold">{appointment.Date:dddd, dd MMMM yyyy 'at' HH:mm}</p>
          <p>Duration: {appointment.DurationMinutes} minutes</p>
          {(appointment.Notes is not null ? $"<p>Notes: {WebUtility.HtmlEncode(appointment.Notes)}</p>" : string.Empty)}
          <p>If you need to reschedule, please contact us as soon as possible.</p>
          <hr/>
          <p style="font-size:0.85em;color:#666">Pena e Arte — Your Tattoo Studio</p>
        </body>
        </html>
        """;
}
