using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class TrialExpiryWarningJob(
    INotificationService notifications,
    AppDbContext db,
    IRealtimeNotifier realtime,
    ILogger<TrialExpiryWarningJob> logger)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        Studio? studio = await db.Studios.FindAsync([studioId], ct);
        if (studio is null)
        {
            logger.LogWarning("Studio {@StudioId} not found for trial expiry warning job", studioId);
            return;
        }

        string? yearlySavingParagraph = await BuildYearlySavingParagraphAsync(ct);

        string subject = "Your TattooOS trial expires in 48 hours";
        string emailBody = BuildEmailBody(studio, yearlySavingParagraph);

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
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = emailBody,
            SentAt = DateTime.UtcNow,
            IsSuccess = success
        };
        db.NotificationLogs.Add(log);

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            studio.Id, "NotificationReceived", ToResponse(log, studio.Name), ct);
    }

    private static NotificationLogResponse ToResponse(NotificationLog log, string? recipientName) => new(
        log.Id, log.RecipientId, recipientName, log.Channel.ToString(),
        log.Subject, log.Body, log.SentAt, log.IsSuccess, log.CreatedAt);

    // D7 — computed from real prices, never Plan.YearlyDiscountPercent. Only a tier whose
    // Yearly price is genuinely purchasable (active, linked to a real Stripe price, and
    // Monthly > 0) counts — an unlinked Yearly row (Starter/Growth until Batch 3 links
    // them) must not be advertised here.
    private async Task<string?> BuildYearlySavingParagraphAsync(CancellationToken ct)
    {
        List<Plan> plans = await db.Plans.Include(p => p.Prices).ToListAsync(ct);

        List<Plan> paidTiers = plans
            .Where(p => p.Prices.Any(pp =>
                pp.Interval == BillingInterval.Monthly && pp.IsActive && pp.Price > 0))
            .ToList();
        if (paidTiers.Count == 0) return null;

        List<(string Name, decimal Monthly, decimal Yearly, decimal MonthsFree)> purchasableYearly = [];
        foreach (Plan p in paidTiers)
        {
            PlanPrice monthly = p.Prices.First(pp =>
                pp.Interval == BillingInterval.Monthly && pp.IsActive && pp.Price > 0);
            PlanPrice? yearly = p.Prices.FirstOrDefault(pp =>
                pp.Interval == BillingInterval.Yearly && pp.IsActive && pp.StripePriceId != null);
            if (yearly is null) continue;

            decimal? monthsFree = YearlySavingCalculator.MonthsFree(monthly.Price, yearly.Price);
            if (monthsFree is decimal mf)
                purchasableYearly.Add((p.Name, monthly.Price, yearly.Price, mf));
        }
        if (purchasableYearly.Count == 0) return null;

        bool everyPaidTierQualifies = purchasableYearly.Count == paidTiers.Count;
        bool allSameWholeMonths =
            purchasableYearly.Select(t => t.MonthsFree).Distinct().Count() == 1
            && purchasableYearly[0].MonthsFree == Math.Floor(purchasableYearly[0].MonthsFree);

        if (everyPaidTierQualifies && allSameWholeMonths)
        {
            int months = (int)purchasableYearly[0].MonthsFree;
            return $"<p>Pay yearly and get {months} {(months == 1 ? "month" : "months")} free.</p>";
        }

        // Only some paid tiers offer purchasable yearly billing — name them, using the
        // first qualifying tier's saving figure (every core tier is designed to the same
        // 2-months-free saving, so this only diverges for an admin-created custom plan).
        string tierNames = string.Join(", ", purchasableYearly.Select(t => t.Name));
        (string Name, decimal Monthly, decimal Yearly, decimal MonthsFree) first = purchasableYearly[0];
        string savingText = first.MonthsFree == Math.Floor(first.MonthsFree)
            ? $"{(int)first.MonthsFree} {((int)first.MonthsFree == 1 ? "month" : "months")} free"
            : YearlySavingCalculator.PercentFloor(first.Monthly, first.Yearly) is int percent
                ? $"save {percent}%"
                : $"{first.MonthsFree} months free";

        return $"<p>Yearly billing is available on {tierNames} — {savingText}.</p>";
    }

    private static string BuildEmailBody(Studio studio, string? yearlySavingParagraph) =>
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
          {yearlySavingParagraph}
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS — Studio Management Platform</p>
        </body>
        </html>
        """;
}
