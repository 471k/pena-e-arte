namespace Pena_e_Arte.Contracts.Responses.Public;

// Deliberately carries Status (unlike ReviewableAppointmentResponse) — the report-filing picker
// shows it so the client has context while choosing which visit a report relates to, since
// eligibility here is NOT restricted to Completed (see architecture.md Decisions Log entry).
public record ReportableAppointmentResponse(Guid Id, DateTime Date, int DurationMinutes, string Status);
