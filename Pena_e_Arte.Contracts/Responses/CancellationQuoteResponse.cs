namespace Pena_e_Arte.Contracts.Responses;

/// <summary>What the owner sees before confirming a cancellation — exactly the amount that
/// would actually be refunded (or, for Monthly/cash, that no refund applies). BillingInterval
/// is "Monthly" or "Yearly" (cash-billed studios report their real interval too — the
/// no-Stripe-subscription case is distinguished by RefundAmount always being 0, not by this
/// field). InvoiceId is the subscription invoice a yearly refund was computed from (null when no
/// refund formula applies); the cancel request sends it back so the server can tell whether the
/// quote it would honour now is still the one the owner confirmed (E05b-2).</summary>
public record CancellationQuoteResponse(
    string BillingInterval,
    decimal RefundAmount,
    int? MonthsUsed,
    DateTime AccessEndDate,
    decimal? AmountPaid,
    decimal? MonthlyReferencePrice,
    Guid? InvoiceId);
