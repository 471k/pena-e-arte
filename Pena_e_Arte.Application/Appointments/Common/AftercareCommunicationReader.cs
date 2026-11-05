using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Appointments.Common;

/// <summary>
/// What the aftercare step actually sent for one appointment, per channel, from its NotificationLog rows
/// (E04b, A08). The one reading of those rows: the staff communication card
/// (GetAppointmentCommunicationHandler) and the client's own appointment page (GetMyAppointmentHandler,
/// E04c phase C) both use it, so the two can't describe the same send differently.
///
/// "Sent" means our provider accepted the message. Nothing here records delivery or reading, and the
/// wording built on it must not claim either.
/// </summary>
public static class AftercareCommunicationReader
{
    /// <returns>State "NotSent" (aftercare hasn't run), "Sent" (at least one channel accepted), "Failed"
    /// (every attempted channel failed) or "NotRecorded" (it ran, but no per-channel row exists: older
    /// appointments, or aftercare messages switched off), plus each attempted channel, oldest first.</returns>
    public static async Task<AftercareCommunicationResponse> ReadAsync(
        IAppDbContext db, Appointment appointment, CancellationToken ct)
    {
        List<CommunicationChannelResponse> channels = (await db.NotificationLogs
                .Where(n => n.AppointmentId == appointment.Id && n.RecipientType == NotificationRecipientType.Client)
                .OrderBy(n => n.SentAt)
                .Select(n => new { n.Channel, n.IsSuccess, n.SentAt })
                .ToListAsync(ct))
            .Select(n => new CommunicationChannelResponse(n.Channel.ToString(), n.IsSuccess, n.SentAt))
            .ToList();

        string state = appointment.AftercareSentAt is null
            ? "NotSent"
            : channels.Count == 0
                ? "NotRecorded"
                : channels.Any(c => c.Sent) ? "Sent" : "Failed";

        return new AftercareCommunicationResponse(state, appointment.AftercareSentAt, channels);
    }
}
