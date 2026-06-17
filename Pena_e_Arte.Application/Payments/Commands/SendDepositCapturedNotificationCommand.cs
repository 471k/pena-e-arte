using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record SendDepositCapturedNotificationCommand(Guid PaymentId) : IRequest<Unit>;

public class SendDepositCapturedNotificationHandler(
    IAppDbContext                                             db,
    IEmailRenderer                                           emailRenderer,
    INotificationService                                     notifications,
    INotificationPreferenceService                           prefs,
    IRealtimeNotifier                                        realtime,
    ILogger<SendDepositCapturedNotificationHandler>          logger)
    : IRequestHandler<SendDepositCapturedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendDepositCapturedNotificationCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.Appointment)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);

        if (payment is null)
        {
            logger.LogWarning("Payment {@PaymentId} not found for deposit captured notification",
                command.PaymentId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == payment.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for deposit captured notification of payment {@PaymentId}",
                payment.StudioId, payment.Id);
            return Unit.Value;
        }

        string amountFormatted  = payment.Amount.ToString("C", new CultureInfo("pt-PT"));
        string appointmentDate  = payment.Appointment.Date.ToString(
            "dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.DepositCaptured, NotificationChannel.Email, ct);
        bool smsEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.DepositCaptured, NotificationChannel.Sms, ct);

        // Email to client
        string emailSubject = "Deposit received — your appointment is secured";
        string emailBody = emailRenderer.RenderDepositCaptured(
            payment.Client.FirstName,
            amountFormatted,
            appointmentDate,
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
                StudioId      = studio.Id,
                RecipientId   = payment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel       = NotificationChannel.Email,
                Subject       = emailSubject,
                Body          = emailBody,
                SentAt        = DateTime.UtcNow,
                IsSuccess     = emailSuccess,
            };
            db.NotificationLogs.Add(emailLog);
            await db.SaveChangesAsync(ct);
        }

        // SMS to client
        if (smsEnabled && payment.Client.Phone is not null)
        {
            string smsBody =
                $"Hi {payment.Client.FirstName}, your deposit of {amountFormatted} " +
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
                StudioId      = studio.Id,
                RecipientId   = payment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel       = NotificationChannel.Sms,
                Subject       = null,
                Body          = smsBody,
                SentAt        = DateTime.UtcNow,
                IsSuccess     = smsSuccess,
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
