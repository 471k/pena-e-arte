using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

/// <summary>Owner-facing cancellation confirmation email — not client-facing, so it follows
/// PastDueReminderJob's inline-HTML + NotificationLog pattern (studio.OwnerEmail,
/// RecipientType.Studio) rather than IEmailRenderer's templated Render* methods, which are for
/// client-facing emails (a different domain — e.g. RenderPaymentRefunded).</summary>
public record SendSubscriptionCancelledNotificationCommand(
    Guid SubscriptionId, decimal RefundAmount, int? MonthsUsed,
    SubscriptionStatus NewStatus, DateTime AccessEndDate) : IRequest;

public class SendSubscriptionCancelledNotificationHandler(
    IAppDbContext db,
    INotificationService notifications,
    ILogger<SendSubscriptionCancelledNotificationHandler> logger)
    : IRequestHandler<SendSubscriptionCancelledNotificationCommand>
{
    public async Task Handle(SendSubscriptionCancelledNotificationCommand command, CancellationToken ct)
    {
        Subscription? subscription = await db.Subscriptions
            .Include(s => s.Studio)
            .FirstOrDefaultAsync(s => s.Id == command.SubscriptionId, ct);

        if (subscription?.Studio is not Studio studio || string.IsNullOrWhiteSpace(studio.OwnerEmail))
            return;

        string subject = "Your TattooOS subscription has been cancelled";
        string body = BuildEmailBody(studio, command);

        bool success = false;
        try
        {
            await notifications.SendEmailAsync(studio.OwnerEmail, subject, body, ct);
            success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send cancellation confirmation email for subscription {@SubscriptionId}",
                command.SubscriptionId);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
        });

        await db.SaveChangesAsync(ct);
    }

    private static string BuildEmailBody(Studio studio, SendSubscriptionCancelledNotificationCommand command)
    {
        string refundLine = command.RefundAmount > 0
            ? $"""
              <p>A refund of <strong>€{command.RefundAmount:0.00}</strong> has been issued to your
                 original payment method{(command.MonthsUsed is int m ? $" ({m} month{(m == 1 ? "" : "s")} of your yearly plan used)" : "")}.
                 Please allow 5–10 business days for it to appear on your statement.</p>
              """
            : "";

        return $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:sans-serif;color:#222;max-width:600px;margin:auto">
          <h2>Your subscription has been cancelled</h2>
          <p>Hi {studio.Name} team,</p>
          <p>Your TattooOS subscription has been cancelled. You'll have access until
             <strong>{command.AccessEndDate:d MMMM yyyy}</strong>.</p>
          {refundLine}
          <p style="margin:2em 0">
            <a href="https://app.tattooos.co/billing/subscribe"
               style="background:#1a1a1a;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
              Resubscribe →
            </a>
          </p>
          <hr/>
          <p style="font-size:0.85em;color:#666">TattooOS — Studio Management Platform</p>
        </body>
        </html>
        """;
    }
}
