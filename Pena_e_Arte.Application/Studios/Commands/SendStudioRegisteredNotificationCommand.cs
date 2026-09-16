using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record SendStudioRegisteredNotificationCommand(Guid StudioId) : IRequest<Unit>;

public class SendStudioRegisteredNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    IIdentityService identity,
    IRealtimeNotifier realtime,
    IAppSettings appSettings,
    ILogger<SendStudioRegisteredNotificationHandler> logger)
    : IRequestHandler<SendStudioRegisteredNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendStudioRegisteredNotificationCommand command, CancellationToken ct)
    {
        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == command.StudioId, ct);
        if (studio is null)
        {
            logger.LogWarning("Studio {@StudioId} not found for studio-registered admin notification", command.StudioId);
            return Unit.Value;
        }

        IReadOnlyList<string> adminEmails = await identity.GetEmailsInRoleAsync("admin", ct);
        if (adminEmails.Count == 0)
        {
            logger.LogWarning("No admin accounts found to notify of new studio {@StudioId}", studio.Id);
            return Unit.Value;
        }

        string detailUrl = $"{appSettings.BaseUrl}/platform/studios/{studio.Id}";
        string body = emailRenderer.RenderStudioRegisteredAdmin(
            studio.Name, studio.City, studio.OwnerEmail, studio.Nipt,
            studio.TrialExpiresAt, studio.PendingReferralCodeId.HasValue, detailUrl);
        string subject = $"New studio registered — {studio.Name}";

        // Bypasses INotificationPreferenceService deliberately — this is a mandatory
        // platform-ops notice, not a client-facing event a studio can opt out of. One log
        // row represents the event; IsSuccess reflects whether at least one admin email
        // actually went out.
        bool anySucceeded = false;
        foreach (string email in adminEmails)
        {
            try
            {
                await notifications.SendEmailAsync(email, subject, body, ct);
                anySucceeded = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send studio-registered notification to admin {@AdminEmail} for studio {@StudioId}",
                    email, studio.Id);
            }
        }

        NotificationLog log = new()
        {
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Admin,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = anySucceeded,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);

        await realtime.NotifyAdminsAsync("NotificationReceived", GetNotificationsHandler.Map(log, studio.Name), ct);

        return Unit.Value;
    }
}
