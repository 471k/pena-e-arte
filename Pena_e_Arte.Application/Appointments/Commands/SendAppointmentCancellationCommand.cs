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
        string? timezone = await db.Studios
            .Where(s => s.Id == appointment.StudioId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(ct);
        DateTime localDate = TimezoneUtils.ToStudioLocalForRules(appointment.Date, timezone);

        string subject = $"Appointment Cancelled — {localDate.ToString("ddd, dd MMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)}";
        string? depositLine = await DescribeDepositAsync(appointment, command, ct);
        string body = BuildEmailBody(appointment, localDate, depositLine);

        bool emailEnabled = await prefs.IsEnabledAsync(
            appointment.StudioId, NotificationType.AppointmentCancelled, NotificationChannel.Email, ct);

        NotificationLog? log = null;
        if (emailEnabled)
        {
            bool success = false;
            try
            {
                await notifications.SendEmailAsync(appointment.Client.Email, subject, body, ct);
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

    private static string BuildEmailBody(Appointment appointment, DateTime localDate, string? depositLine) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2 style="color:#c0392b">Appointment Cancelled</h2>
          <p>Hi {System.Net.WebUtility.HtmlEncode(appointment.Client.FirstName)},</p>
          <p>Your tattoo appointment scheduled for:</p>
          <p style="font-size:1.1em;font-weight:bold">{localDate.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)}</p>
          <p>has been cancelled.</p>
          {(depositLine is null ? "" : $"<p>{WebUtility.HtmlEncode(depositLine)}</p>")}
          <p>If you have any questions or would like to rebook, please contact us.</p>
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS &mdash; Your Tattoo Studio</p>
        </body>
        </html>
        """;
}
