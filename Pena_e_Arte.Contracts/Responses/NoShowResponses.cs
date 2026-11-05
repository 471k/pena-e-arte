namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// GET /api/v1/appointments/{id}/no-show-preview (E04b, A04): what marking this appointment as a no-show
/// would do to its deposit, decided by the same function PATCH {id}/no-show applies (NoShowOutcome). A
/// projection: the commit decides again, and its own result (<see cref="NoShowResultResponse"/>) is what
/// counts. No-show cannot be undone.
/// </summary>
/// <param name="CanMarkNoShow">False for a Cancelled or Completed appointment; the commit would refuse.</param>
/// <param name="Message">The commit's own refusal text when it would refuse; otherwise null.</param>
/// <param name="Outcome">NothingCollected, ChargedDepositKept, CashDepositKept, HeldDepositCaptured,
/// DeclaredCashNotCollected or Unknown.</param>
/// <param name="Amount">The money concerned (0 when nothing was collected).</param>
/// <param name="Currency">The payment's currency, or the studio's when there is no payment.</param>
public record NoShowPreviewResponse(
    Guid AppointmentId,
    bool CanMarkNoShow,
    string? Message,
    string Outcome,
    decimal Amount,
    string Currency,
    bool IsProjection = true);

/// <summary>
/// PATCH /api/v1/appointments/{id}/no-show: the appointment afterwards, and what the commit actually did
/// to the deposit (the decision applied at commit, never the earlier preview).
/// </summary>
/// <param name="PaymentStatus">The deposit payment's status afterwards, or null when there is no payment.</param>
public record NoShowResultResponse(
    AppointmentResponse Appointment,
    string Outcome,
    decimal Amount,
    string Currency,
    string? PaymentStatus);
