using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Commands;

public record SendConsentFormSignedNotificationCommand(Guid ConsentFormId) : IRequest<Unit>;

public class SendConsentFormSignedNotificationHandler(
    IAppDbContext                                             db,
    IEmailRenderer                                           emailRenderer,
    INotificationService                                     notifications,
    IRealtimeNotifier                                        realtime,
    ILogger<SendConsentFormSignedNotificationHandler>        logger)
    : IRequestHandler<SendConsentFormSignedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendConsentFormSignedNotificationCommand command, CancellationToken ct)
    {
        ConsentForm? form = await db.ConsentForms
            .Include(f => f.Client)
            .Include(f => f.Appointment)
            .FirstOrDefaultAsync(f => f.Id == command.ConsentFormId, ct);

        if (form is null)
        {
            logger.LogWarning("ConsentForm {@ConsentFormId} not found for signed notification",
                command.ConsentFormId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == form.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for consent form signed notification of form {@ConsentFormId}",
                form.StudioId, form.Id);
            return Unit.Value;
        }

        string appointmentDate = form.Appointment.Date.ToString(
            "dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

        string clientFullName = $"{form.Client.FirstName} {form.Client.LastName}";
        string subject = $"Consent form signed — {clientFullName}";
        string body = emailRenderer.RenderConsentFormSigned(
            studio.Name,
            clientFullName,
            appointmentDate,
            studio.ShowPlatformBranding);

        bool success = true;
        try
        {
            await notifications.SendEmailAsync(studio.OwnerEmail, subject, body, ct);
            logger.LogInformation(
                "Consent form signed email sent for form {@ConsentFormId}",
                form.Id);
        }
        catch (Exception ex)
        {
            success = false;
            logger.LogWarning(ex,
                "Failed to send consent form signed email for form {@ConsentFormId}",
                form.Id);
        }

        NotificationLog log = new()
        {
            StudioId      = studio.Id,
            RecipientId   = studio.Id,
            RecipientType = NotificationRecipientType.Studio,
            Channel       = NotificationChannel.Email,
            Subject       = subject,
            Body          = body,
            SentAt        = DateTime.UtcNow,
            IsSuccess     = success,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            studio.Id, "NotificationReceived",
            GetNotificationsHandler.Map(log, studio.Name), ct);

        return Unit.Value;
    }
}
