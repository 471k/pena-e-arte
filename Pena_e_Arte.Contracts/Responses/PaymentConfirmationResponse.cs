namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// What POST /api/v1/payments/{id}/confirm returns (C08). Deliberately narrow: the pages that call it
/// (the in-app checkout and /payment-result, a public route) need one sentence about one payment, so
/// no client identity (name, client token), no studio free text (cash note), no provider reference and
/// no splits cross the wire. Do not widen it with those; ConfirmCardPaymentHandlerTests pins their absence.
/// </summary>
public record PaymentConfirmationResponse(
    Guid PaymentId,
    // PaymentStatus name.
    string Status,
    decimal Amount,
    string Currency,
    // The handler's own "owed back" answer: a card payment still Pending or held on an appointment that
    // was cancelled (not a late-cancel forfeit) while it was going through. PaymentReconciliationJob
    // releases or refunds those in full. False when the handler did not reach that question (cash, or a
    // payment already Paid/Refunded/Failed: a cancellation after payment is settled by the studio's
    // cancellation policy, and nothing returns it automatically).
    bool AppointmentCancelled,
    decimal? RefundedAmount = null,
    // The lek frozen when the client paid (Albanian EUR studios only; all null otherwise). Currency and
    // rate travel with the amount so the page shows the same "(ALL x)" as the other payment screens, and a
    // refunded part converts at the same frozen rate.
    decimal? ReferenceAmount = null,
    string? ReferenceCurrency = null,
    decimal? ReferenceRate = null);
