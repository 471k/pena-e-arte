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

    public Subscription Subscription { get; set; } = null!;
}
