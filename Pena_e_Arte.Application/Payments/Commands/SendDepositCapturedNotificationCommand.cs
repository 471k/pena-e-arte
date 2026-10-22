using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Payments.Commands;

public record SendDepositCapturedNotificationCommand(Guid PaymentId) : IRequest<Unit>;

public class SendDepositCapturedNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    ILogger<SendDepositCapturedNotificationHandler> logger)
    : IRequestHandler<SendDepositCapturedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendDepositCapturedNotificationCommand command, CancellationToken ct)
    {
        // By the payment's own id, across tenants: also sent from DepositPaidNotificationJob, which
        // runs outside a request (no tenant). Only server code sends this command, never a client.
        Payment? payment = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Client)
            .Include(p => p.Appointment)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId && p.DeletedAt == null, ct);

        if (payment is null)
        {
            logger.LogWarning("Payment {@PaymentId} not found for deposit captured notification",
                command.PaymentId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == payment.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for deposit captured notification of payment {@PaymentId}",
                payment.StudioId, payment.Id);
            return Unit.Value;
        }

        string amountFormatted = MoneyText.Format(payment.Amount, payment.Currency);
        // Appointment.Date is UTC; the client reads the studio's wall-clock time.
        string appointmentDate = TimezoneUtils.ToStudioLocalForRules(payment.Appointment.Date, studio.Timezone)
            .ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.DepositCaptured, NotificationChannel.Email, ct);
        bool smsEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.DepositCaptured, NotificationChannel.Sms, ct);

        // Card deposits are paid at booking (C-4), usually while the appointment is still a request the
        // studio hasn't confirmed: never tell the client it's "secured" before the studio confirms.
        bool awaitingConfirmation = payment.Appointment.Status == AppointmentStatus.Pending;

        // Email to client
        string emailSubject = awaitingConfirmation
            ? "Deposit received — the studio will confirm your appointment"
            : "Deposit received — your appointment is secured";
        string emailBody = emailRenderer.RenderDepositCaptured(
            payment.Client.FirstName,
            amountFormatted,
            appointmentDate,
            awaitingConfirmation,
            studio.ShowPlatformBranding);

        NotificationLog? emailLog = null;
        if (emailEnabled)
        {
            bool emailSuccess = true;
            try
            {
                await notifications.SendEmailAsync(payment.Client.Email, emailSubject, emailBody, ct);
                logger.LogInformation(
                    "Deposit captured email sent for payment {@PaymentId}",
                    payment.Id);
            }
            catch (Exception ex)
            {
                emailSuccess = false;
                logger.LogWarning(ex,
                    "Failed to send deposit captured email for payment {@PaymentId}",
                    payment.Id);
            }

            emailLog = new()
            {
                StudioId = studio.Id,
                RecipientId = payment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel = NotificationChannel.Email,
                Subject = emailSubject,
                Body = emailBody,
                SentAt = DateTime.UtcNow,
                IsSuccess = emailSuccess,
            };
            db.NotificationLogs.Add(emailLog);
            await db.SaveChangesAsync(ct);
        }

        // SMS to client
        if (smsEnabled && payment.Client.Phone is not null)
        {
            string smsBody = awaitingConfirmation
                ? $"Hi {payment.Client.FirstName}, your deposit of {amountFormatted} for your appointment request " +
                  $"on {appointmentDate} has been received. The studio will confirm soon."
                : $"Hi {payment.Client.FirstName}, your deposit of {amountFormatted} " +
                  $"for your appointment on {appointmentDate} has been received. See you soon!";

            bool smsSuccess = true;
            try
            {
                await notifications.SendSmsAsync(payment.Client.Phone, smsBody, ct);
            }
            catch (Exception ex)
            {
                smsSuccess = false;
                logger.LogWarning(ex,
                    "Failed to send deposit captured SMS for payment {@PaymentId}",
                    payment.Id);
            }

            db.NotificationLogs.Add(new NotificationLog
            {
                StudioId = studio.Id,
                RecipientId = payment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = smsBody,
                SentAt = DateTime.UtcNow,
                IsSuccess = smsSuccess,
            });
            await db.SaveChangesAsync(ct);
        }

        if (emailLog is not null)
        {
            await realtime.NotifyStudioAsync(
                studio.Id, "NotificationReceived",
                GetNotificationsHandler.Map(emailLog, $"{payment.Client.FirstName} {payment.Client.LastName}"), ct);
        }

        return Unit.Value;
    }
}
