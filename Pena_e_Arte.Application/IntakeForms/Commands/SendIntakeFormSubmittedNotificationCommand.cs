using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.IntakeForms.Commands;

public record SendIntakeFormSubmittedNotificationCommand(Guid IntakeFormId) : IRequest<Unit>;

public class SendIntakeFormSubmittedNotificationHandler(
    IAppDbContext                                               db,
    IEmailRenderer                                             emailRenderer,
    INotificationService                                       notifications,
    INotificationPreferenceService                             prefs,
    IRealtimeNotifier                                          realtime,
    ILogger<SendIntakeFormSubmittedNotificationHandler>        logger)
    : IRequestHandler<SendIntakeFormSubmittedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendIntakeFormSubmittedNotificationCommand command, CancellationToken ct)
    {
        IntakeForm? form = await db.IntakeForms
            .Include(f => f.Client)
            .FirstOrDefaultAsync(f => f.Id == command.IntakeFormId, ct);

        if (form is null)
        {
            logger.LogWarning("IntakeForm {@IntakeFormId} not found for submitted notification",
                command.IntakeFormId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == form.StudioId, ct);

        if (studio is null)
        {
            logger.LogWarning(
                "Studio {@StudioId} not found for intake form submitted notification of form {@IntakeFormId}",
                form.StudioId, form.Id);
            return Unit.Value;
        }

        string appointmentDate = "(no appointment date)";
        if (form.AppointmentId.HasValue)
        {
            Appointment? appointment = await db.Appointments
                .FirstOrDefaultAsync(a => a.Id == form.AppointmentId.Value, ct);
            if (appointment is not null)
                appointmentDate = appointment.Date.ToString(
                    "dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
        }

        string clientFullName = $"{form.Client.FirstName} {form.Client.LastName}";
        string subject = $"Intake form submitted — {clientFullName}";
        string body = emailRenderer.RenderIntakeFormSubmitted(
            studio.Name,
            clientFullName,
            appointmentDate,
            studio.ShowPlatformBranding);

        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.IntakeFormSubmitted, NotificationChannel.Email, ct);

        NotificationLog? log = null;
        if (emailEnabled)
        {
            bool success = true;
            try
            {
                await notifications.SendEmailAsync(studio.OwnerEmail, subject, body, ct);
                logger.LogInformation(
                    "Intake form submitted email sent for form {@IntakeFormId}",
                    form.Id);
            }
            catch (Exception ex)
            {
                success = false;
                logger.LogWarning(ex,
                    "Failed to send intake form submitted email for form {@IntakeFormId}",
                    form.Id);
            }

            log = new()
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
        }

        if (log is not null)
        {
            await realtime.NotifyStudioAsync(
                studio.Id, "NotificationReceived",
                GetNotificationsHandler.Map(log, studio.Name), ct);
        }

        return Unit.Value;
    }
}
