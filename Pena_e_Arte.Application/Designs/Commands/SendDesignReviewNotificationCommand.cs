using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record SendDesignReviewNotificationCommand(Guid DesignRevisionId, bool Approved) : IRequest<Unit>;

public class SendDesignReviewNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    ILogger<SendDesignReviewNotificationHandler> logger)
    : IRequestHandler<SendDesignReviewNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendDesignReviewNotificationCommand command, CancellationToken ct)
    {
        DesignRevision? revision = await db.DesignRevisions
            .Include(r => r.Design).ThenInclude(d => d.Artist)
            .Include(r => r.Approval)
            .FirstOrDefaultAsync(r => r.Id == command.DesignRevisionId, ct);

        if (revision is null)
        {
            logger.LogWarning("DesignRevision {@DesignRevisionId} not found for review notification",
                command.DesignRevisionId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == revision.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for design review notification of revision {@DesignRevisionId}",
                revision.StudioId, revision.Id);
            return Unit.Value;
        }

        Artist artist = revision.Design.Artist;
        string? clientNotes = revision.Approval?.ClientNotes;
        string designTitle = revision.Design.Title;

        string subject = command.Approved
            ? $"Design approved — {designTitle}"
            : $"Changes requested — {designTitle}";

        string body = command.Approved
            ? emailRenderer.RenderDesignApproved(
                artist.FirstName,
                designTitle,
                clientNotes,
                studio.ShowPlatformBranding)
            : emailRenderer.RenderDesignChangesRequested(
                artist.FirstName,
                designTitle,
                clientNotes,
                studio.ShowPlatformBranding);

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.DesignReviewed, NotificationChannel.Email, ct);

        NotificationLog? studioLog = null;
        if (emailEnabled)
        {
            // Email to studio owner
            bool studioSuccess = true;
            try
            {
                await notifications.SendEmailAsync(studio.OwnerEmail, subject, body, ct);
                logger.LogInformation(
                    "Design review studio email sent for revision {@DesignRevisionId}",
                    revision.Id);
            }
            catch (Exception ex)
            {
                studioSuccess = false;
                logger.LogWarning(ex,
                    "Failed to send design review studio email for revision {@DesignRevisionId}",
                    revision.Id);
            }

            studioLog = new()
            {
                StudioId = studio.Id,
                RecipientId = studio.Id,
                RecipientType = NotificationRecipientType.Studio,
                Channel = NotificationChannel.Email,
                Subject = subject,
                Body = body,
                SentAt = DateTime.UtcNow,
                IsSuccess = studioSuccess,
            };
            db.NotificationLogs.Add(studioLog);
            await db.SaveChangesAsync(ct);

            // Email to artist (if non-empty email and different from studio owner)
            if (!string.IsNullOrWhiteSpace(artist.Email) &&
                !string.Equals(artist.Email, studio.OwnerEmail, StringComparison.OrdinalIgnoreCase))
            {
                bool artistSuccess = true;
                try
                {
                    await notifications.SendEmailAsync(artist.Email, subject, body, ct);
                    logger.LogInformation(
                        "Design review artist email sent for revision {@DesignRevisionId}",
                        revision.Id);
                }
                catch (Exception ex)
                {
                    artistSuccess = false;
                    logger.LogWarning(ex,
                        "Failed to send design review artist email for revision {@DesignRevisionId}",
                        revision.Id);
                }

                db.NotificationLogs.Add(new NotificationLog
                {
                    StudioId = studio.Id,
                    RecipientId = artist.Id,
                    RecipientType = NotificationRecipientType.Artist,
                    Channel = NotificationChannel.Email,
                    Subject = subject,
                    Body = body,
                    SentAt = DateTime.UtcNow,
                    IsSuccess = artistSuccess,
                });
                await db.SaveChangesAsync(ct);
            }
        }

        if (studioLog is not null)
        {
            await realtime.NotifyStudioAsync(
                studio.Id, "NotificationReceived",
                GetNotificationsHandler.Map(studioLog, studio.Name), ct);
        }

        return Unit.Value;
    }
}
