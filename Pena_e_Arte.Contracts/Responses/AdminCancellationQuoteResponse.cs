namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// What an admin cancellation will do, shown before it is confirmed (slice 8 phase 3, D03). Built by the same rule the
/// cancel command applies (AdminCancellationQuotes), and sent back with the cancel so the server can refuse a stale one.
/// <para>Cancellation by an admin is immediate: the subscription becomes Cancelled now, whatever the interval.</para>
/// <para>RefundApplies is true only for a Yearly, Stripe-billed subscription with a paid invoice; otherwise every rule
/// refunds nothing and no refund row is written. FormulaRefund / FullRefund are what "formula" and "full" would refund;
/// "none" is always 0.</para>
/// </summary>
public record AdminCancellationQuoteResponse(
    string Status,
    string? PlanName,
    string BillingInterval,
    bool BilledThroughStripe,
    bool RefundApplies,
    string? Currency,
    decimal FormulaRefund,
    decimal FullRefund,
    int? MonthsUsed,
    decimal? AmountPaid,
    Guid? InvoiceId);
