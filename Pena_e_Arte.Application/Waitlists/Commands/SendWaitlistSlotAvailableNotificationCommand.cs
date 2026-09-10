using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Commands;

public record SendWaitlistSlotAvailableNotificationCommand(Guid WaitlistEntryId) : IRequest<Unit>;

public class SendWaitlistSlotAvailableNotificationHandler(
    IAppDbContext db,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    IAppSettings appSettings,
    ILogger<SendWaitlistSlotAvailableNotificationHandler> logger)
    : IRequestHandler<SendWaitlistSlotAvailableNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendWaitlistSlotAvailableNotificationCommand command, CancellationToken ct)
    {
        Domain.Entities.Waitlist? entry = await db.WaitlistEntries
            .IgnoreQueryFilters()
            .Include(w => w.Client)
            .FirstOrDefaultAsync(w => w.Id == command.WaitlistEntryId, ct);

        if (entry is null)
        {
            logger.LogWarning("Waitlist entry {@WaitlistEntryId} not found for slot-available email",
                command.WaitlistEntryId);
            return Unit.Value;
        }

        string? recipientEmail = entry.Client?.Email ?? entry.GuestEmail;
        string? recipientFirstName = entry.Client?.FirstName ?? entry.GuestName;
        if (string.IsNullOrEmpty(recipientEmail))
        {
            logger.LogWarning("Waitlist entry {@WaitlistEntryId} has no resolvable email", entry.Id);
            return Unit.Value;
        }

        string bookUrl = $"{appSettings.BaseUrl}/waitlist/{entry.Id}/claim";
        string subject = "A slot just opened up";
        string body = BuildEmailBody(recipientFirstName, bookUrl);

        bool emailEnabled = await prefs.IsEnabledAsync(
            entry.StudioId, NotificationType.WaitlistSlotAvailable, NotificationChannel.Email, ct);

        NotificationLog? log = null;
        if (emailEnabled)
        {
            bool success = false;
            try
            {
                await notifications.SendEmailAsync(recipientEmail, subject, body, ct);
                success = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send waitlist slot-available email for entry {@WaitlistEntryId}", entry.Id);
            }

            log = new()
            {
                StudioId = entry.StudioId,
                RecipientId = entry.ClientId,
                RecipientType = entry.ClientId is not null
                    ? NotificationRecipientType.Client
                    : NotificationRecipientType.ExternalContact,
                Channel = NotificationChannel.Email,
                Subject = subject,
                Body = body,
                SentAt = DateTime.UtcNow,
                IsSuccess = success,
            };
            db.NotificationLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }

        if (log is not null)
        {
            await realtime.NotifyStudioAsync(
                entry.StudioId, "NotificationReceived",
                GetNotificationsHandler.Map(log, recipientFirstName), ct);
        }

        return Unit.Value;
    }

    private static string BuildEmailBody(string? firstName, string bookUrl) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2 style="color:#27ae60">A Slot Just Opened Up</h2>
          <p>Hi {System.Net.WebUtility.HtmlEncode(firstName ?? "there")},</p>
          <p>A slot matching your waitlist request is now available. You have
             <strong>24 hours</strong> to claim it before it's offered to the next person in line.</p>
          <p><a href="{bookUrl}" style="display:inline-block;padding:10px 20px;background:#27ae60;
             color:#fff;text-decoration:none;border-radius:4px">Claim this slot</a></p>
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS &mdash; Your Tattoo Studio</p>
        </body>
        </html>
        """;
}
