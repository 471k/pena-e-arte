namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Booking-content constants shared by CreateAppointmentValidator (authenticated) and
/// CreateGuestAppointmentValidator (guest checkout) — the two validators intentionally
/// duplicate the FluentValidation RuleFor calls themselves (this codebase has no established
/// nested-command-validator composition convention, and the two commands wrap different outer
/// types), but the underlying constants they validate against were being redeclared
/// identically rather than shared, risking drift between the authenticated and guest booking
/// flows. Found via /code-review, 2026-09-01.
/// </summary>
public static class BookingContentValidationRules
{
    // Mirrors BookAppointmentForm.tsx's VALID_DURATIONS — the session-length options actually
    // offered in the booking form.
    public static readonly int[] ValidDurations = [30, 45, 60, 90, 120, 180, 240, 300, 360, 480];

    // Mirrors CategorizedImagesField's per-category cap.
    public const int MaxImagesPerCategory = 6;

    public static readonly HashSet<string> ValidImageCategories = ["AreaPhoto", "Reference"];

    public static readonly HashSet<string> ValidReferralSources =
        ["Instagram", "TikTok", "YouTube", "FriendsAndFamily", "Other"];
}
