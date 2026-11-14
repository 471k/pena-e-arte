using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
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
    IWaitlistClaimTokens claimTokens,
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

        if (entry.Status != WaitlistStatus.Notified || entry.NotifiedAt is null)
        {
            logger.LogWarning("Waitlist entry {@WaitlistEntryId} is not notified; no slot-available email sent", entry.Id);
            return Unit.Value;
        }

        // Studio has no tenant filter, so this read needs no IgnoreQueryFilters.
        Studio? studio = await db.Studios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == entry.StudioId, ct);
        string? slotText = entry.NotifiedSlotStart is DateTime slotStart
            ? TimezoneUtils.ToStudioLocalForRules(slotStart, studio?.Timezone)
                .ToString("dddd d MMMM 'at' HH:mm", CultureInfo.InvariantCulture)
            : null;

        // C09: the link carries a purpose-bound claim token in the URL fragment, never the raw entry id. A
        // fragment is never sent to a server, so the token stays out of Cloudflare, ingress and request logs.
        string bookUrl = $"{appSettings.BaseUrl}/waitlist/claim#t={claimTokens.Mint(entry)}";
        string subject = "A slot just opened up";
        string body = BuildEmailBody(recipientFirstName, studio?.Name, slotText, bookUrl);
        // What's stored in NotificationLogs and shown to studio staff: the same email with the token removed.
        // The log is a record that the email went out, not a second copy of the client's credential.
        string loggedBody = BuildEmailBody(recipientFirstName, studio?.Name, slotText,
            $"{appSettings.BaseUrl}/waitlist/claim#t=[removed]");

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
                Body = loggedBody,
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

    private static string BuildEmailBody(string? firstName, string? studioName, string? slotText, string bookUrl)
    {
        string where = studioName is null ? "" : $" at {System.Net.WebUtility.HtmlEncode(studioName)}";
        string when = slotText is null ? "" : $" on <strong>{System.Net.WebUtility.HtmlEncode(slotText)}</strong>";
        int hours = (int)WaitlistClaimWindow.Duration.TotalHours;
        return $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2 style="color:#27ae60">A Slot Just Opened Up</h2>
          <p>Hi {System.Net.WebUtility.HtmlEncode(firstName ?? "there")},</p>
          <p>A slot matching your waitlist request{where} is free{when}.</p>
          <p>It isn't held for you: it goes to whoever books it first. You're the one being offered it for the next
             <strong>{hours} hours</strong>; after that the offer moves to the next person in line.</p>
          <p><a href="{bookUrl}" style="display:inline-block;padding:10px 20px;background:#27ae60;
             color:#fff;text-decoration:none;border-radius:4px">Book this slot</a></p>
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS &mdash; Your Tattoo Studio</p>
        </body>
        </html>
        """;
    }
}
