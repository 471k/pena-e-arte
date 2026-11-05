namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// GET /api/v1/artists/{id}/time-off/affected-sessions (E04c, A06): the artist's booked sessions that
/// fall inside a time-off range, shown BEFORE the time off is added (or removed). A projection: it
/// reserves nothing, and adding or removing time off never moves, cancels or otherwise changes any of
/// these sessions. The artist decides what to do with each one.
/// </summary>
/// <param name="StartDate">The range's first day (studio-local date, as time off stores it).</param>
/// <param name="EndDate">The range's last day, inclusive.</param>
/// <param name="Sessions">Pending and Confirmed sessions overlapping the range, earliest first. A session
/// counts when any part of it falls inside the range: one that starts the evening before and runs past
/// midnight into the first day counts, and so does one that starts on the last day and ends after it.</param>
public record TimeOffAffectedSessionsResponse(
    Guid ArtistId,
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<TimeOffAffectedSessionResponse> Sessions,
    bool IsProjection = true);

/// <param name="Date">Start (UTC).</param>
/// <param name="EndDate">End (UTC).</param>
/// <param name="Status">"Pending" (a request) or "Confirmed".</param>
public record TimeOffAffectedSessionResponse(
    Guid AppointmentId,
    DateTime Date,
    DateTime EndDate,
    string ClientName,
    string Status);
