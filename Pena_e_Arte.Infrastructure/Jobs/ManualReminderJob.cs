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

        // A reminder can be scheduled up to 90 days ahead — by send time the linked
        // appointment may have already been cancelled, completed, or marked a no-show, all
        // of which make "reminding" the client about it meaningless or actively confusing.
        if (reminder.Appointment is not null && reminder.Appointment.Status
            is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow)
        {
            reminder.Status = ManualReminderStatus.Cancelled;
            reminder.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Skipping ManualReminder {@ManualReminderId} — linked appointment is {@AppointmentStatus}",
                manualReminderId, reminder.Appointment.Status);
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

        // A reminder can be scheduled up to 90 days ahead, but PlanLimitBehavior only checked
        // NotificationsPerMonth against the CREATION month's usage — it has no way to know
        // which month this will actually send in. Re-checked here, against the send month's
        // real usage, so a batch scheduled while under this month's cap can't silently blow
        // past next month's cap with zero enforcement. Queried directly against
        // reminder.StudioId (not ICurrentTenant/IPlanLimitService, which assume an HTTP
        // request's JWT-scoped tenant — this job runs with no such context) and skips the
        // 30s usage cache PlanLimitService uses, since this runs once per reminder, not once
        // per request.
        Subscription? subscription = await db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StudioId == reminder.StudioId, ct);

        if (subscription?.Plan?.MaxNotificationsPerMonth is int maxNotifications)
        {
            DateTime sendMonthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            // NotificationLog has its own tenant-scoped global query filter (like
            // ManualReminder above) that this job's ambient context can't satisfy —
            // IgnoreQueryFilters() plus the explicit StudioId check is the same pattern.
            int usedThisMonth = await db.NotificationLogs
                .IgnoreQueryFilters()
                .Where(n => n.StudioId == reminder.StudioId && n.DeletedAt == null && n.CreatedAt >= sendMonthStart)
                .CountAsync(ct);

            if (usedThisMonth >= maxNotifications)
            {
                reminder.Status = ManualReminderStatus.Failed;
                reminder.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Skipping ManualReminder {@ManualReminderId} — studio has exceeded its " +
                    "NotificationsPerMonth plan limit for the send month", manualReminderId);
                return;
            }
        }

        // A Hangfire retry after a crash between the SMS send and the save below (see
        // SendAttemptedAt's doc comment) lands back here with a prior attempt already
        // claimed — the actual delivery outcome is unknown, so this deliberately does not
        // re-send. Marked Failed so it stops looking pending and surfaces for reconciliation,
        // rather than silently retrying forever or texting the client a second time.
        if (reminder.SendAttemptedAt.HasValue)
        {
            logger.LogWarning(
                "ManualReminder {@ManualReminderId} already had a send attempt at {@SendAttemptedAt} " +
                "with no recorded outcome — likely a retry after a crash. Not re-sending.",
                manualReminderId, reminder.SendAttemptedAt);
            reminder.Status = ManualReminderStatus.Failed;
            reminder.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == reminder.StudioId, ct);
        string studioName = studio?.Name ?? "your studio";

        string body = BuildBody(reminder, studioName);

        // Claimed and durably persisted before the send itself — if the process dies or the
        // post-send save below fails, the guard above prevents a retry from sending twice.
        reminder.SendAttemptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

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
        // Same "Reply STOP to opt out." disclosure AppointmentReminderJob's automatic SMS
        // carries — required on every outbound SMS this app sends, not just automatic ones,
        // regardless of whether the artist used the default template or wrote their own text.
        string message = !string.IsNullOrWhiteSpace(reminder.Message)
            ? reminder.Message
            : reminder.Appointment is not null
                ? $"Hi {reminder.RecipientName}, reminder from {studioName} — your appointment is " +
                  $"{reminder.Appointment.Date:ddd dd MMM 'at' HH:mm}."
                : $"Hi {reminder.RecipientName}, this is a reminder from {studioName}.";

        return $"{message} Reply STOP to opt out.";
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
