using System.Globalization;
using System.Net;
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

namespace Pena_e_Arte.Application.Appointments.Commands;

/// <param name="RefundedAmount">What this cancellation refunded to the client's card, if anything.
/// Passed by the cancelling handler rather than read back from the payment, so a deposit the owner
/// refunded days earlier isn't announced again as "being refunded".</param>
/// <param name="DepositForfeited">True when this cancellation kept the client's paid deposit
/// (late client cancellation under the studio's policy).</param>
public record SendAppointmentCancellationCommand(
    Guid AppointmentId, decimal? RefundedAmount = null, bool DepositForfeited = false) : IRequest<Unit>;

public class SendAppointmentCancellationHandler(
    IAppDbContext db,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    IAppSettings appSettings,
    ILogger<SendAppointmentCancellationHandler> logger)
    : IRequestHandler<SendAppointmentCancellationCommand, Unit>
{
    public async Task<Unit> Handle(SendAppointmentCancellationCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .Include(a => a.Client)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);

        if (appointment is null)
        {
            logger.LogWarning("Appointment {@AppointmentId} not found for cancellation email",
                command.AppointmentId);
            return Unit.Value;
        }

        // appointment.Date is UTC; the client reads the studio's wall-clock time.
        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == appointment.StudioId, ct);
        DateTime localDate = TimezoneUtils.ToStudioLocalForRules(appointment.Date, studio?.Timezone);
        string dateText = localDate.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
        string studioName = studio?.Name ?? "the studio";
        bool declined = appointment.CancellationReason == CancellationReason.StudioDeclined;

        string subject = declined
            ? $"Booking request declined — {studioName}"
            : $"Appointment cancelled — {studioName}, {localDate.ToString("ddd, dd MMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)}";
        string? depositLine = await DescribeDepositAsync(appointment, command, ct);
        string body = BuildEmailBody(appointment, studio, dateText, depositLine);

        bool emailEnabled = await prefs.IsEnabledAsync(
            appointment.StudioId, NotificationType.AppointmentCancelled, NotificationChannel.Email, ct);
        bool smsEnabled = await prefs.IsEnabledAsync(
            appointment.StudioId, NotificationType.AppointmentCancelled, NotificationChannel.Sms, ct);

        NotificationLog? log = null;
        if (emailEnabled)
        {
            bool success = false;
            try
            {
                // Replies reach the studio owner, so "reply to this email" in the body works.
                string? replyTo = string.IsNullOrWhiteSpace(studio?.OwnerEmail) ? null : studio.OwnerEmail;
                await notifications.SendEmailAsync(appointment.Client.Email, subject, body, replyTo, ct);
                success = true;
                logger.LogInformation(
                    "Cancellation email sent for appointment {@AppointmentId}",
                    appointment.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send cancellation email for appointment {@AppointmentId}",
                    appointment.Id);
            }

            log = new()
            {
                StudioId = appointment.StudioId,
                RecipientId = appointment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel = NotificationChannel.Email,
                Subject = subject,
                Body = body,
                SentAt = DateTime.UtcNow,
                IsSuccess = success,
            };
            db.NotificationLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }

        // SMS like the other booking messages (received, confirmed): the settings page always
        // offered an "Appointment cancelled" SMS switch, but nothing was sent (28 Oct 2026). Any
        // refund goes out separately as the "refund processed" SMS, so this one stays short.
        if (smsEnabled && appointment.Client.Phone is not null)
        {
            string smsBody = appointment.CancellationReason switch
            {
                CancellationReason.StudioDeclined =>
                    $"Hi {appointment.Client.FirstName}, sorry, {studioName} can't take your booking request for {dateText}.",
                CancellationReason.ClientCancelled =>
                    $"Hi {appointment.Client.FirstName}, your cancellation is confirmed: your appointment at {studioName} on {dateText} has been cancelled.",
                _ =>
                    $"Hi {appointment.Client.FirstName}, your appointment at {studioName} on {dateText} has been cancelled.",
            };

            bool smsSent = true;
            try
            {
                await notifications.SendSmsAsync(appointment.Client.Phone, smsBody, ct);
            }
            catch (Exception ex)
            {
                smsSent = false;
                logger.LogWarning(ex,
                    "Failed to send cancellation SMS for appointment {@AppointmentId}",
                    appointment.Id);
            }

            db.NotificationLogs.Add(new NotificationLog
            {
                StudioId = appointment.StudioId,
                RecipientId = appointment.ClientId,
                RecipientType = NotificationRecipientType.Client,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = smsBody,
                SentAt = DateTime.UtcNow,
                IsSuccess = smsSent,
            });
            await db.SaveChangesAsync(ct);
        }

        if (log is not null)
        {
            await realtime.NotifyStudioAsync(
                appointment.StudioId, "NotificationReceived",
                GetNotificationsHandler.Map(log, $"{appointment.Client.FirstName} {appointment.Client.LastName}"), ct);
        }

        return Unit.Value;
    }

    // The cancellation email says what happened to the client's money (C-11 production test, 28 Oct
    // 2026: a client who cancelled with notice was refunded in full but the email only said
    // "has been cancelled"). Null when this cancellation moved no money.
    private async Task<string?> DescribeDepositAsync(
        Appointment appointment, SendAppointmentCancellationCommand command, CancellationToken ct)
    {
        if (command.RefundedAmount is null && !command.DepositForfeited)
            return null;

        Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);
        if (payment is null)
            return null;

        // Albanian EUR studios: lek at the rate frozen when the client paid (spec D5).
        string Money(decimal amount) => MoneyText.FormatWithReference(
            amount, payment.Currency,
            payment.ReferenceRate is decimal rate ? ReferenceAmount.ToWholeLek(amount, rate) : null,
            payment.ReferenceCurrency, approximate: false);

        if (command.RefundedAmount is decimal refunded && refunded > 0)
        {
            if (refunded >= payment.Amount)
                return $"Your deposit of {Money(payment.Amount)} is being refunded to your card. " +
                       "Allow 3-5 business days for it to appear.";

            return $"{Money(refunded)} of your {Money(payment.Amount)} deposit is being refunded to your card. " +
                   "Allow 3-5 business days for it to appear. " +
                   $"The studio keeps the remaining {Money(payment.Amount - refunded)} under its cancellation policy.";
        }

        return command.DepositForfeited
            ? $"Your deposit of {Money(payment.Amount)} is not refunded, because the appointment was cancelled " +
              "with less notice than the studio's cancellation policy requires."
            : null;
    }

    // Names the studio and how to reach it (it used to sign off "TattooOS — Your Tattoo Studio" and
    // say "please contact us" with no contact at all), and links to booking another time.
    private string BuildEmailBody(Appointment appointment, Studio? studio, string dateText, string? depositLine)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);
        string studioName = Encode(studio?.Name ?? "the studio");
        string date = Encode(dateText);

        (string heading, string message) = appointment.CancellationReason switch
        {
            CancellationReason.StudioDeclined => (
                "About your booking request",
                $"Sorry, <strong>{studioName}</strong> can't take your booking request for <strong>{date}</strong>."),
            CancellationReason.ClientCancelled => (
                "Cancellation confirmed",
                $"Your appointment at <strong>{studioName}</strong> on <strong>{date}</strong> has been cancelled, as you asked."),
            _ => (
                "Appointment cancelled",
                $"Your appointment at <strong>{studioName}</strong> on <strong>{date}</strong> has been cancelled by the studio."),
        };

        string contact = string.IsNullOrWhiteSpace(studio?.PhoneNumber)
            ? $"Questions? Reply to this email to reach {studioName}."
            : $"Questions? Reply to this email to reach {studioName}, or call {Encode(studio.PhoneNumber)}.";
        string rebook = studio is null || string.IsNullOrWhiteSpace(studio.Slug)
            ? ""
            : $"""<p><a href="{Encode($"{appSettings.BaseUrl}/s/{studio.Slug}")}" target="_top" style="display:inline-block;background:#7c3aed;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none">Book another time</a></p>""";
        string footer = studio?.ShowPlatformBranding == false ? studioName : $"{studioName} &middot; Powered by TattooOS";

        return $"""
            <!DOCTYPE html>
            <html>
            <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
              <h2 style="color:#c0392b">{heading}</h2>
              <p>Hi {Encode(appointment.Client.FirstName)},</p>
              <p>{message}</p>
              {(depositLine is null ? "" : $"<p>{Encode(depositLine)}</p>")}
              {rebook}
              <p>{contact}</p>
              <hr/>
              <p style="font-size:0.85em;color:#666">{footer}</p>
            </body>
            </html>
            """;
    }
}
