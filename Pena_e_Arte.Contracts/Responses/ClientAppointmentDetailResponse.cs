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
/// <param name="Contact">Who a message from this page reaches, and the studio's own contact details.</param>
/// <param name="Visit">Where to go (DEC11). Null unless the appointment is Confirmed: an unconfirmed
/// request never receives the studio's private address.</param>
/// <param name="Aftercare">The aftercare the studio sent after the session (DEC11). Null unless the
/// appointment is Completed, aftercare ran, and the studio's aftercare email is on or was attempted. It
/// says per channel what actually went out (E04c): the steps can be shown when the email failed, but
/// the page must not imply they were emailed.</param>
public record ClientAppointmentDetailResponse(
    AppointmentResponse Appointment,
    IReadOnlyList<AppointmentReadinessItemResponse> Readiness,
    ClientAppointmentContactResponse Contact,
    ClientAppointmentVisitResponse? Visit = null,
    ClientAppointmentAftercareResponse? Aftercare = null);

/// <summary>
/// The studio's real address, for a Confirmed appointment only. <paramref name="AddressComplete"/> is
/// false when the studio has not entered at least a street line and a city; every address field is then
/// null, so a partial address is never shown. No coordinates are sent: the page shows no map pin.
/// </summary>
public record ClientAppointmentVisitResponse(
    bool AddressComplete,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? PostalCode);

/// <summary>
/// The aftercare steps from the email the studio sent (Domain.Constants.AftercareInstructions, the one
/// copy the email also renders from), and when the aftercare step ran.
/// </summary>
/// <param name="SentAt">When the aftercare step ran (UTC). Not proof that any message went out: see Channels.</param>
/// <param name="State">E04c phase C, the same reading the staff card uses (AftercareCommunicationReader):
/// "Sent" (at least one channel accepted), "Failed" (every attempted channel failed) or "NotRecorded"
/// (no per-channel record: older appointments).</param>
/// <param name="Channels">Each attempted channel (Email, Sms) and whether our provider accepted it. Never
/// a delivery or read receipt.</param>
public record ClientAppointmentAftercareResponse(
    DateTime SentAt,
    IReadOnlyList<string> Steps,
    string State = "NotRecorded",
    IReadOnlyList<CommunicationChannelResponse>? Channels = null);

/// <summary>
/// DEC06. The recipient is the appointment's artist when they can sign in to read it, otherwise the
/// studio owner (Application.Messaging.StudioOwnerResolver), and null when neither resolves; the page
/// then shows the studio's contact details and no compose box. It is the same rule CreateConversation
/// checks, so a recipient offered here is always accepted. A recipient is never guessed.
/// </summary>
/// <param name="RecipientRole">"artist" or "owner"; null with no recipient.</param>
public record ClientAppointmentContactResponse(
    Guid? RecipientUserId,
    string? RecipientRole,
    string? RecipientName,
    string StudioName,
    string? StudioPhone,
    string? StudioInstagram);

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
