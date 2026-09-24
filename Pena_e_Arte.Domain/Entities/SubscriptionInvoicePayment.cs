namespace Pena_e_Arte.Domain.Entities;

/// <summary>One row per paid Stripe invoice — powers the "Discounts this month" platform-stats
/// figure and is reusable by the future revenue ledger/yearly-refund calc. Deliberately not a
/// TenantEntity, same reasoning as Subscription itself: admin needs a cross-tenant read for the
/// monthly aggregate.</summary>
public class SubscriptionInvoicePayment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid StudioId { get; set; }
    public string StripeInvoiceId { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>When Stripe actually paid this invoice — not the period it covers.</summary>
    public DateTime PaidAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>This invoice's billing period start (Stripe invoice line item). Null for
    /// invoices recorded before this column existed — the yearly-refund calculator treats
    /// that as "no quote available" rather than guessing.</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>The tier's Monthly PlanPrice.Price at the moment this invoice paid — Yearly
    /// invoices only. Used by YearlyRefundCalculator so a later Monthly price change never
    /// retroactively changes what an already-issued refund would have been.</summary>
    public decimal? MonthlyReferencePrice { get; set; }

    /// <summary>Refund target for this invoice's payment. Null for invoices recorded before
    /// this column existed, or when Stripe's payment shape didn't resolve one.</summary>
    public string? StripePaymentIntentId { get; set; }

    public Subscription Subscription { get; set; } = null!;
}
