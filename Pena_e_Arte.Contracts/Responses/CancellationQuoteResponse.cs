namespace Pena_e_Arte.Contracts.Responses;

/// <summary>What the owner sees before confirming a cancellation — exactly the amount that
/// would actually be refunded (or, for Monthly/cash, that no refund applies). BillingInterval
/// is "Monthly" or "Yearly" (cash-billed studios report their real interval too — the
/// no-Stripe-subscription case is distinguished by RefundAmount always being 0, not by this
/// field).</summary>
public record CancellationQuoteResponse(
    string BillingInterval,
    decimal RefundAmount,
    int? MonthsUsed,
    DateTime AccessEndDate,
    decimal? AmountPaid,
    decimal? MonthlyReferencePrice);
