namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// GET /api/v1/appointments/{id}/preparation (ArtistAndAbove): a session's preparation for the artist
/// (E04a, A03). The facts are the client's DEC03 readiness, derived by the same
/// AppointmentReadinessReader, but worded for the person preparing the session: whether the client has
/// done each thing, never an instruction to do it. Readiness never blocks the session.
/// It deliberately does not reuse <see cref="AppointmentReadinessItemResponse"/>, whose ActionLabel,
/// Available, Destination and Note are written to the client ("Sign the consent form").
/// </summary>
/// <param name="Items">The requirements the session's service turns on, in priority order. Empty when it
/// configures none; the artist then sees no checklist.</param>
/// <param name="Deposit">The deposit payment, as the fields the staff screens describe a deposit from, or
/// null when there is no payment row. Session splits and every other payment detail are left out.</param>
public record AppointmentPreparationResponse(
    Guid AppointmentId,
    IReadOnlyList<AppointmentPreparationItemResponse> Items,
    AppointmentPreparationDepositResponse? Deposit);

/// <param name="Key">Stable: DepositPaid, IntakeForm, ConsentForm or ReferenceImage.</param>
/// <param name="Label">What the requirement is, e.g. "Consent form".</param>
/// <param name="Satisfied">Whether the client has done it, from the real record.</param>
/// <param name="State">Where it stands, in the third person, e.g. "Signed" or "Not signed yet".</param>
/// <param name="FollowUp">For an outstanding item only: what the client can still do about it and where,
/// so the artist knows what to ask for. Null once satisfied, and null when the session is over.</param>
public record AppointmentPreparationItemResponse(
    string Key,
    string Label,
    bool Satisfied,
    string State,
    string? FollowUp);

/// <summary>
/// The deposit payment's outcome fields, matching what the frontend's describeDeposit reads (G-13), so the
/// artist sees the same words the owner's payment screens use. Lek reference fields are set for Albanian
/// EUR studios only.
/// </summary>
public record AppointmentPreparationDepositResponse(
    decimal Amount,
    string Status,
    string Method,
    DateTime? PaidAt,
    string Currency,
    decimal? RefundedAmount,
    string AppointmentStatus,
    string? ReferenceCurrency,
    decimal? ReferenceRate);
