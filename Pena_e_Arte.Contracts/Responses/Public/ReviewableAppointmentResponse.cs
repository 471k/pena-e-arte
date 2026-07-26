namespace Pena_e_Arte.Contracts.Responses.Public;

// A completed, not-yet-reviewed appointment the current client can leave a review
// for — powers the "which visit are you reviewing?" picker on the write-a-review form.
public record ReviewableAppointmentResponse(
    Guid Id,
    DateTime Date,
    int DurationMinutes);
