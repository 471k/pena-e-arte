using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class ManualReminderJob(
    INotificationService notifications,
    AppDbContext db,
    IRealtimeNotifier realtime,
    ILogger<ManualReminderJob> logger)
{
    public async Task SendAsync(Guid manualReminderId, CancellationToken ct = default)
    {
        ManualReminder? reminder = await db.ManualReminders
            .IgnoreQueryFilters()
            .Include(m => m.Client)
            .Include(m => m.Appointment)
            .FirstOrDefaultAsync(m => m.Id == manualReminderId && m.DeletedAt == null, ct);

        if (reminder is null)
        {
            logger.LogWarning("ManualReminder {@ManualReminderId} not found for reminder job", manualReminderId);
            return;
        }

        if (reminder.Status == ManualReminderStatus.Cancelled)
        {
            logger.LogInformation("Skipping cancelled ManualReminder {@ManualReminderId}", manualReminderId);
            return;
        }

        if (reminder.Appointment is not null && reminder.Appointment.Status == AppointmentStatus.Cancelled)
        {
            reminder.Status = ManualReminderStatus.Cancelled;
            reminder.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Skipping ManualReminder {@ManualReminderId} — linked appointment was cancelled", manualReminderId);
            return;
        }

        if (reminder.Client is not null && reminder.Client.SmsOptOut)
        {
            reminder.Status = ManualReminderStatus.Failed;
            reminder.SentAt = DateTime.UtcNow;
            reminder.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Skipping ManualReminder {@ManualReminderId} — client has opted out of SMS", manualReminderId);
            return;
        }

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == reminder.StudioId, ct);
        string studioName = studio?.Name ?? "your studio";

        string body = BuildBody(reminder, studioName);
        bool success = await TrySendSmsAsync(reminder, body, ct);

        NotificationLog log = new()
        {
            StudioId = reminder.StudioId,
            RecipientId = reminder.ClientId,
            RecipientType = reminder.ClientId is not null
                ? NotificationRecipientType.Client
                : NotificationRecipientType.ExternalContact,
            Channel = NotificationChannel.Sms,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success
        };
        db.NotificationLogs.Add(log);

        reminder.Status = success ? ManualReminderStatus.Sent : ManualReminderStatus.Failed;
        reminder.SentAt = DateTime.UtcNow;
        reminder.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            reminder.StudioId, "NotificationReceived",
            ToResponse(log, reminder.RecipientName), ct);
    }

    private static NotificationLogResponse ToResponse(NotificationLog log, string recipientName) => new(
        log.Id, log.RecipientId, recipientName, log.Channel.ToString(),
        log.Subject, log.Body, log.SentAt, log.IsSuccess, log.CreatedAt);

    private static string BuildBody(ManualReminder reminder, string studioName)
    {
        if (!string.IsNullOrWhiteSpace(reminder.Message))
            return reminder.Message;

        return reminder.Appointment is not null
            ? $"Hi {reminder.RecipientName}, reminder from {studioName} — your appointment is " +
              $"{reminder.Appointment.Date:ddd dd MMM 'at' HH:mm}."
            : $"Hi {reminder.RecipientName}, this is a reminder from {studioName}.";
    }

    private async Task<bool> TrySendSmsAsync(ManualReminder reminder, string body, CancellationToken ct)
    {
        try
        {
            await notifications.SendSmsAsync(reminder.RecipientPhone, body, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send manual reminder SMS {@ManualReminderId}", reminder.Id);
            return false;
        }
    }
}
