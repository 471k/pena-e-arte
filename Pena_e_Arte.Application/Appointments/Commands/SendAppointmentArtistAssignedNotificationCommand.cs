using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record SendAppointmentArtistAssignedNotificationCommand(Guid AppointmentId) : IRequest<Unit>;

public class SendAppointmentArtistAssignedNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    IRealtimeNotifier realtime,
    ILogger<SendAppointmentArtistAssignedNotificationHandler> logger)
    : IRequestHandler<SendAppointmentArtistAssignedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendAppointmentArtistAssignedNotificationCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .Include(a => a.Client)
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);

        if (appointment is null || appointment.Artist is null)
        {
            logger.LogWarning(
                "Appointment {@AppointmentId} not found or has no artist for artist-assigned notification",
                command.AppointmentId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == appointment.StudioId, ct);
        if (studio is null) return Unit.Value;

        // Reuses the AppointmentCreated preference toggle — this is a follow-up to the same
        // "your booking" thread the client already opted into at booking time, not a
        // distinct notification category.
        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.AppointmentCreated, NotificationChannel.Email, ct);

        if (!emailEnabled) return Unit.Value;

        string body = emailRenderer.RenderAppointmentArtistAssigned(
            appointment.Client.FirstName,
            $"{appointment.Artist.FirstName} {appointment.Artist.LastName}",
            TimezoneUtils.ToStudioLocal(appointment.Date, studio.Timezone),
            studio.Name,
            studio.ShowPlatformBranding);

        string subject = $"Your artist has been assigned — {studio.Name}";
        bool success = true;
        try
        {
            await notifications.SendEmailAsync(appointment.Client.Email, subject, body, ct);
        }
        catch (Exception ex)
        {
            success = false;
            logger.LogWarning(ex,
                "Failed to send artist-assigned email for appointment {@AppointmentId}", appointment.Id);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            StudioId = studio.Id,
            RecipientId = appointment.ClientId,
            RecipientType = NotificationRecipientType.Client,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
        });
        await db.SaveChangesAsync(ct);

        // The artist the owner just picked is the one doing the work — tell them too (email + the
        // bell entry only they can see). Without this, a "let the studio choose my artist" booking
        // leaves the chosen artist unaware until they happen to open their schedule.
        DateTime localDate = TimezoneUtils.ToStudioLocal(appointment.Date, studio.Timezone);
        string clientFullName = $"{appointment.Client.FirstName} {appointment.Client.LastName}";
        string artistBody = emailRenderer.RenderAppointmentAssignedToArtist(
            appointment.Artist.FirstName, clientFullName, localDate,
            appointment.DurationMinutes, appointment.Notes);

        NotificationLog artistLog = await ArtistBookingNotifier.NotifyAsync(
            db, notifications, logger, studio, appointment.Artist, appointment.Id,
            $"You've been assigned a booking — {clientFullName}", artistBody,
            ownerAlreadyEmailedSuccessfully: true, ct);

        await realtime.NotifyStudioAsync(
            studio.Id, "NotificationReceived",
            GetNotificationsHandler.Map(artistLog, $"{appointment.Artist.FirstName} {appointment.Artist.LastName}"), ct);

        return Unit.Value;
    }
}
