namespace Pena_e_Arte.Contracts.Responses;

/// <summary>One yearly-cancellation refund on the admin studio page. Currency is the refund's own (never assumed);
/// InvoiceReference is the Stripe invoice it was issued against, so a failed one can be looked up (D07).
/// UpdatedAt is when the row last changed, which for a Failed refund is when it failed. ReachedStripe is whether Stripe
/// accepted the refund request (a refund id exists): a Failed one that never reached Stripe may or may not have been made,
/// while one that did was reported failed by Stripe, which returns the money to the platform's Stripe balance.</summary>
public record RecentRefundResponse(
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? FailureReason,
    string InvoiceReference,
    bool ReachedStripe);
