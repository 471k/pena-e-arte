using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class TrialExpiryWarningJob(
    INotificationService              notifications,
    AppDbContext                      db,
    IRealtimeNotifier                 realtime,
    ILogger<TrialExpiryWarningJob>    logger)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        Studio? studio = await db.Studios.FindAsync([studioId], ct);
        if (studio is null)
        {
            logger.LogWarning("Studio {@StudioId} not found for trial expiry warning job", studioId);
            return;
        }

        string subject   = "Your TattooOS trial expires in 48 hours";
        string emailBody = BuildEmailBody(studio);

        bool success = false;
        try
        {
            await notifications.SendEmailAsync(studio.OwnerEmail, subject, emailBody, ct);
            success = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send trial expiry warning email for studio {@StudioId}", studioId);
        }

        NotificationLog log = new()
        {
            StudioId      = studio.Id,
            RecipientId   = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel       = NotificationChannel.Email,
            Subject       = subject,
            Body          = emailBody,
            SentAt        = DateTime.UtcNow,
            IsSuccess     = success
        };
        db.NotificationLogs.Add(log);

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            studio.Id, "NotificationReceived", ToResponse(log, studio.Name), ct);
    }

    private static NotificationLogResponse ToResponse(NotificationLog log, string? recipientName) => new(
        log.Id, log.RecipientId, recipientName, log.Channel.ToString(),
        log.Subject, log.Body, log.SentAt, log.IsSuccess, log.CreatedAt);

    private static string BuildEmailBody(Studio studio) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2 style="color:#c0392b">Your free trial ends in 48 hours</h2>
          <p>Hi {studio.Name} team,</p>
          <p>Your 14-day free trial of <strong>TattooOS</strong> expires on
             <strong>{studio.TrialExpiresAt:dddd, dd MMMM yyyy 'at' HH:mm} UTC</strong>.</p>
          <p>After your trial ends you'll have a 7-day read-only grace period before your account
             is suspended. Subscribe now to keep full access and avoid any interruption.</p>
          <p style="margin:2em 0">
            <a href="https://app.tattooos.co/billing"
               style="background:#1a1a1a;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
              Choose a plan →
            </a>
          </p>
          <p>Yearly plans save you 2 months — that's ~17% off. 🎉</p>
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS — Studio Management Platform</p>
        </body>
        </html>
        """;
}
