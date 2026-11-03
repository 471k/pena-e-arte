namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// GET /api/v1/appointments/mine/{id}: one of the signed-in client's own appointments, for the client
/// appointment page (DEC02). It exists so the appointment can be deep-linked, and to carry the
/// heavier computed blocks without adding them to every row of /mine. It is not an authorization
/// fix: /mine already returns the client their own appointments.
/// </summary>
/// <param name="Appointment">The same shape /mine returns, with SelfService and the Readiness summary
/// filled in. SafetyNotes is withheld: it is medical information this page does not display, and /mine
/// does not carry it either.</param>
/// <param name="Readiness">The itemised preparation requirements in priority order. Empty when the
/// service configures none; the page then shows no checklist.</param>
public record ClientAppointmentDetailResponse(
    AppointmentResponse Appointment,
    IReadOnlyList<AppointmentReadinessItemResponse> Readiness);

/// <param name="Key">Stable: DepositPaid, IntakeForm, ConsentForm or ReferenceImage.</param>
/// <param name="Label">What the requirement is, e.g. "Consent form".</param>
/// <param name="ActionLabel">What the client does, e.g. "Sign the consent form".</param>
/// <param name="Required">Always true for a listed item: only requirements the service turns on are listed.</param>
/// <param name="Available">The client can do it in the app now. False once satisfied, for an
/// appointment that is no longer upcoming, and for a requirement with no client path.</param>
/// <param name="Destination">An existing app route where the client satisfies it, or null when the
/// action happens on the appointment page itself (the deposit) or there is none.</param>
/// <param name="Note">Shown with an outstanding item the client can't act on in the app.</param>
public record AppointmentReadinessItemResponse(
    string Key,
    string Label,
    string ActionLabel,
    bool Required,
    bool Satisfied,
    bool Available,
    string? Destination,
    string? Note);
