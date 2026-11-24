namespace Pena_e_Arte.Contracts.Responses.Public;

// A completed, not-yet-reviewed appointment the current client can leave a review
// for — powers the "which visit are you reviewing?" picker on the write-a-review form.
public record ReviewableAppointmentResponse(
    Guid Id,
    DateTime Date,
    int DurationMinutes,
    // S01. The appointment's studio's IANA zone, so the "which visit" picker names the day on the studio's
    // calendar, not the viewer's: a client in another zone could otherwise be shown the wrong day and pick the
    // wrong visit. Null only if the studio row is missing, which the picker shows as "Time unavailable".
    string? StudioTimezone);
