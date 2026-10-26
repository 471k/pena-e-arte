using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// C-15: tells the studio owner that a held card deposit can no longer be charged because POK ended
/// the hold (PaymentReconciliationJob marked it Failed). Email + bell entry, owner only; the client
/// is not told — the owner decides whether to ask for the deposit again. Enqueued once, on the
/// Captured → Failed transition, so it never repeats for the same payment. Runs outside a request,
/// so it reads across tenants by the payment's own id, like PaymentReconciliationJob.
/// </summary>
public class DepositHoldLostNotificationJob(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    IAppSettings appSettings,
    ILogger<DepositHoldLostNotificationJob> logger)
{
    public const string Subject = "A card deposit can no longer be charged";

    public async Task ExecuteAsync(Guid paymentId, CancellationToken ct = default)
    {
        Payment? payment = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Client)
            .Include(p => p.Appointment)
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.DeletedAt == null, ct);

        // Re-checked here, not trusted from the caller: the owner may have asked for a new payment
        // (the row is reused) between the reconciliation run and this job.
        if (payment is null || payment.Status != PaymentStatus.Failed || payment.Method != ClientPaymentMethod.Card)
        {
            logger.LogInformation("Deposit hold lost notification skipped for payment {PaymentId}: no longer a failed card payment.", paymentId);
            return;
        }

        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == payment.StudioId, ct);
        if (studio is null)
        {
            logger.LogWarning("Studio {StudioId} not found for deposit hold lost notification of payment {PaymentId}",
                payment.StudioId, payment.Id);
            return;
        }

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.DepositHoldLost, NotificationChannel.Email, ct);
        if (!emailEnabled)
            return;

        DateTime localDate = TimezoneUtils.ToStudioLocal(payment.Appointment.Date, studio.Timezone);
        string body = emailRenderer.RenderDepositHoldLost(
            $"{payment.Client.FirstName} {payment.Client.LastName}",
            // Albanian EUR studios: the lek frozen on the payment (staff see it too, spec Q3 decided 2026-10-26).
            MoneyText.FormatWithReference(
                payment.Amount, payment.Currency, payment.ReferenceAmount, payment.ReferenceCurrency, approximate: false),
            localDate.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture),
            $"{appSettings.BaseUrl}/payments/{payment.AppointmentId}",
            studio.ShowPlatformBranding);

        bool success = true;
        try
        {
            await notifications.SendEmailAsync(studio.OwnerEmail, Subject, body, ct);
            logger.LogInformation("Deposit hold lost email sent for payment {PaymentId}", payment.Id);
        }
        catch (Exception ex)
        {
            success = false;
            logger.LogWarning(ex, "Failed to send deposit hold lost email for payment {PaymentId}", payment.Id);
        }

        NotificationLog log = new()
        {
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel = NotificationChannel.Email,
            Subject = Subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(studio.Id, "NotificationReceived", new NotificationLogResponse(
            log.Id, log.RecipientId, studio.Name, log.Channel.ToString(),
            log.Subject, log.Body, log.SentAt, log.IsSuccess, log.CreatedAt), ct);
    }
}
