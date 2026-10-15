namespace Pena_e_Arte.Contracts.Responses;

public record PaymentResponse(
    Guid Id,
    Guid AppointmentId,
    decimal Amount,
    string Status,
    string Method,
    string? ProviderReferenceId,
    string? ClientToken,
    string? CashNote,
    DateTime? PaidAt,
    string ClientName,
    DateTime? AppointmentDate = null,
    IReadOnlyList<SessionSplitResponse>? Splits = null,
    string Currency = "",
    // What was given back: refunded after a charge, or released from a hold in a partial
    // capture. Lets the screens show "kept X · refunded/released Y" (G-13). Null when nothing.
    decimal? RefundedAmount = null,
    // The appointment's status when the query loaded it ("Cancelled" tells a late-cancel
    // partial capture apart from an owner's partial refund). Null when not loaded.
    string? AppointmentStatus = null);
