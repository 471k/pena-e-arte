using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

/// <summary>
/// Tells the artist a booking is theirs — the email plus the <see cref="NotificationRecipientType.Artist"/>
/// log row their notification bell reads (GetNotificationsHandler only ever shows an artist the rows
/// addressed to them). Shared by the "client booked me directly" and "the studio assigned me" paths.
/// </summary>
internal static class ArtistBookingNotifier
{
    /// <returns>The log row written for the artist's bell — the caller pushes the realtime refresh.</returns>
    internal static async Task<NotificationLog> NotifyAsync(
        IAppDbContext db,
        INotificationService notifications,
        ILogger logger,
        Studio studio,
        Artist artist,
        Guid appointmentId,
        string subject,
        string body,
        bool? ownerEmailResult,
        CancellationToken ct)
    {
        // ownerEmailResult says what happened to the studio-owner copy of this same email in the
        // calling flow: true/false = it was sent to studio.OwnerEmail and succeeded/failed; null =
        // the owner was NOT emailed in this flow (the assign flow only emails the client and the
        // artist). An owner who is also the artist already got this exact email when it was sent
        // to studio.OwnerEmail, so don't send it twice, but still write the artist-addressed row
        // so it shows in their artist bell. When the owner was not emailed, the artist address
        // (even if it is the owner's) must still receive it, or the owner-artist gets nothing.
        bool sameAsOwner = string.Equals(artist.Email, studio.OwnerEmail, StringComparison.OrdinalIgnoreCase);
        bool ownerCopyAlreadySent = sameAsOwner && ownerEmailResult.HasValue;
        bool success = ownerCopyAlreadySent ? ownerEmailResult!.Value : true;

        if (!ownerCopyAlreadySent && !string.IsNullOrWhiteSpace(artist.Email))
        {
            try
            {
                await notifications.SendEmailAsync(artist.Email, subject, body, ct);
                logger.LogInformation(
                    "Booking notification email sent to artist for appointment {@AppointmentId}", appointmentId);
            }
            catch (Exception ex)
            {
                success = false;
                logger.LogWarning(ex,
                    "Failed to send booking notification email to artist for appointment {@AppointmentId}",
                    appointmentId);
            }
        }

        NotificationLog log = new()
        {
            StudioId = studio.Id,
            RecipientId = artist.Id,
            RecipientType = NotificationRecipientType.Artist,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }
}
