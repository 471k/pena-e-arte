namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The appointment a chat message is about, resolved from the appointment row on every read
/// (never stored with the message), so a reschedule shows straight away. <c>StartsAt</c> is UTC;
/// the client renders it in the studio's timezone. <c>ServiceName</c> is null when the
/// appointment has no service — show the date and time alone.
/// </summary>
public record MessageAppointmentLabelResponse(
    Guid AppointmentId,
    DateTime StartsAt,
    string? ServiceName);
