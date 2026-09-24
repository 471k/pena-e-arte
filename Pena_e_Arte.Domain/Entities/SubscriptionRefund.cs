using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>One row per issued (or explicitly-declined) yearly-cancellation refund — one per
/// SubscriptionInvoicePayment (enforced by a unique index), recording exactly what was
/// refunded, why, and by whom. Deliberately not a TenantEntity, same reasoning as
/// Subscription/SubscriptionInvoicePayment — admin needs a cross-tenant read for the
/// "Refunds this month" platform-stats figure.</summary>
public class SubscriptionRefund
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid StudioId { get; set; }
    public Guid SubscriptionInvoicePaymentId { get; set; }
    public string? StripeRefundId { get; set; }
    public string StripeInvoiceId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int MonthsUsed { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal MonthlyReferencePrice { get; set; }
    public RefundRule Rule { get; set; }
    public Guid InitiatedByUserId { get; set; }

    /// <summary>Required when Rule is AdminFull/AdminNone (validator) — never PII, a free-text
    /// business justification only.</summary>
    public string? AdminReason { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Subscription Subscription { get; set; } = null!;
}
