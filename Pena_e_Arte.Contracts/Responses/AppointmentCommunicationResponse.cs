namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// GET /api/v1/appointments/{id}/communication (E04b, A08): whether the client has been told what
/// changed, shown as a task for staff. It reports only what the data proves: not sent, sent (handed to
/// our provider, or posted to the in-app conversation) and failed. Nothing here says a message was
/// delivered or read, and the wording built on it must not imply that either.
/// </summary>
/// <param name="Reschedule">Null unless the appointment has been rescheduled (from the audit log).</param>
/// <param name="Aftercare">Null unless the appointment is Completed.</param>
public record AppointmentCommunicationResponse(
    Guid AppointmentId,
    RescheduleCommunicationResponse? Reschedule,
    AftercareCommunicationResponse? Aftercare);

/// <summary>
/// The client isn't told about a reschedule automatically, so this tracks whether staff messaged them
/// about this appointment (a message carrying its AppointmentId) since it was last moved.
/// </summary>
/// <param name="State">"NotSent" or "Sent".</param>
/// <param name="RescheduledAt">When it was last moved (UTC).</param>
/// <param name="MessagedAt">The first staff message about it since then (UTC), or null.</param>
public record RescheduleCommunicationResponse(
    string State,
    DateTime RescheduledAt,
    DateTime? MessagedAt);

/// <param name="State">"NotSent" (aftercare hasn't run), "Sent" (at least one channel accepted),
/// "Failed" (every attempted channel failed) or "NotRecorded" (it ran, but no per-channel record
/// exists: older appointments, or aftercare messages switched off).</param>
/// <param name="RanAt">When the aftercare step ran (UTC), or null if it hasn't.</param>
/// <param name="Channels">Each attempted channel's outcome, oldest first.</param>
public record AftercareCommunicationResponse(
    string State,
    DateTime? RanAt,
    IReadOnlyList<CommunicationChannelResponse> Channels);

/// <param name="Channel">"Email" or "Sms".</param>
/// <param name="Sent">True when our provider accepted it. Not a delivery receipt.</param>
public record CommunicationChannelResponse(string Channel, bool Sent, DateTime? At);
