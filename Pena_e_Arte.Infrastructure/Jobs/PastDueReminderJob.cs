using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Daily dunning scan (07:00 UTC, staggered after the existing 06:00 r2-export job): finds every
/// past-due subscription platform-wide and sends an escalating reminder email on exactly day
/// 1, 3, and 7 since it entered PastDue (not "at least" — a studio checked daily gets exactly
/// three emails, not one every day past the first threshold). Studios with
/// DunningExcludedManually set (an admin opt-out — see SetDunningExclusionCommand) are skipped
/// entirely. Idempotent-safe by construction: since it only fires on exact-day matches and runs
/// once daily, a normal run never double-sends. A missed run (job failure) simply skips that
/// day's message rather than catching up later — acceptable for a reminder, not a legal notice.
/// Neither Subscription nor Studio carries a query filter, so no IgnoreQueryFilters() is needed —
/// same "no filter to bypass" reasoning as TrafficRollupJob.
/// </summary>
public class PastDueReminderJob(
    IAppDbContext db,
    INotificationService notifications,
    ILogger<PastDueReminderJob> logger)
{
    private static readonly int[] ReminderDays = [1, 3, 7];

    public async Task RunAsync(CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;

        List<Subscription> pastDue = await db.Subscriptions
            .Include(s => s.Studio)
            .Where(s => s.Status == SubscriptionStatus.PastDue
                && !s.DunningExcludedManually
                && s.PastDueSince != null)
            .ToListAsync(ct);

        int sent = 0;
        foreach (Subscription subscription in pastDue)
        {
            int daysPastDue = (int)(now - subscription.PastDueSince!.Value).TotalDays;
            if (!ReminderDays.Contains(daysPastDue)) continue;

            await SendReminderAsync(subscription, daysPastDue, ct);
            sent++;
        }

        logger.LogInformation(
            "PastDueReminderJob scanned {@Scanned} past-due subscriptions, sent {@Sent} reminder(s)",
            pastDue.Count, sent);
    }

    private async Task SendReminderAsync(Subscription subscription, int daysPastDue, CancellationToken ct)
    {
        Studio studio = subscription.Studio;
        string subject = $"Action needed: your TattooOS payment is {daysPastDue} day{(daysPastDue == 1 ? "" : "s")} overdue";
        string emailBody = BuildEmailBody(studio, daysPastDue);

        bool success = false;
        try
        {
            await notifications.SendEmailAsync(studio.OwnerEmail, subject, emailBody, ct);
            success = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send past-due reminder email for studio {@StudioId} (day {@DaysPastDue})",
                studio.Id, daysPastDue);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = emailBody,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
        });

        await db.SaveChangesAsync(ct);
    }

    private static string BuildEmailBody(Studio studio, int daysPastDue) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2 style="color:#c0392b">Your subscription payment is {daysPastDue} day{(daysPastDue == 1 ? "" : "s")} overdue</h2>
          <p>Hi {studio.Name} team,</p>
          <p>We haven't been able to process your latest TattooOS subscription payment. Please
             update your billing details to avoid any interruption to your account.</p>
          <p style="margin:2em 0">
            <a href="https://app.tattooos.co/billing"
               style="background:#1a1a1a;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
              Update billing details →
            </a>
          </p>
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS — Studio Management Platform</p>
        </body>
        </html>
        """;
}
