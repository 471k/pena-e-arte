namespace Pena_e_Arte.Contracts.Responses.Public;

// Deliberately carries Status (unlike ReviewableAppointmentResponse) — the report-filing picker
// shows it so the client has context while choosing which visit a report relates to, since
// eligibility here is NOT restricted to Completed (see architecture.md Decisions Log entry).
public record ReportableAppointmentResponse(
    Guid Id,
    DateTime Date,
    int DurationMinutes,
    string Status,
    // S01. The appointment's studio's IANA zone, so the "which visit" picker names the day on the studio's
    // calendar, not the viewer's: a client in another zone could otherwise be shown the wrong day and pick the
    // wrong visit. Null only if the studio row is missing, which the picker shows as "Time unavailable".
    string? StudioTimezone);
