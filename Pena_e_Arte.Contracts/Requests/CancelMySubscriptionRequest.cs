namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// The cancellation quote the owner is confirming, sent back exactly as GET
/// /billing/subscription/cancel/quote returned it (E05b-2, O07). The server rebuilds the quote and cancels
/// only if it still matches; otherwise it refuses with CANCELLATION_QUOTE_CHANGED and nothing reaches the
/// billing service.
/// </summary>
public record CancelMySubscriptionRequest(
    string BillingInterval,
    decimal RefundAmount,
    int? MonthsUsed,
    decimal? AmountPaid,
    Guid? InvoiceId);
