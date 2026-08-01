using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Payment : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public ClientPaymentMethod Method { get; set; } = ClientPaymentMethod.Card;

    // Card (provider) fields — null for cash payments.
    /// <summary>The payment provider's own reference id (formerly StripePaymentIntentId).</summary>
    public string? ProviderReferenceId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Which provider issued <see cref="ProviderReferenceId"/> (e.g. "pok"). Empty for
    /// cash or legacy rows. Tells reconciliation/webhooks which IPaymentProvider to call.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency of the payment. Defaults to Albanian lek.</summary>
    public string Currency { get; set; } = "ALL";

    /// <summary>When an authorization hold expires and must be auto-released (maps onto POK's
    /// expiresAfterMinutes). Enforced server-side by PaymentReconciliationJob's release pass.</summary>
    public DateTime? HoldExpiresAt { get; set; }

    /// <summary>
    /// Platform fee deducted from what is disbursed to the studio, wired through at a 0% rate from
    /// day one (ADR-0001 monetization). Deliberately a distinct field, NOT a
    /// <see cref="SessionSplit"/> row: it sits OUTSIDE SessionSplit's exact-sum-to-Amount invariant
    /// (see UpdateSessionSplitsCommand). Do not try to unify the two.
    /// </summary>
    public decimal PlatformFeeAmount { get; set; }

    // Cash fields
    public string? CashNote { get; set; }
    public Guid? CashConfirmedByUserId { get; set; }

    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// How much of Amount has actually been refunded — null/0 means none. Distinguishes a
    /// partial refund from a full one when Status is Refunded (there is no separate
    /// "PartiallyRefunded" status); revenue reporting subtracts this from Amount rather than
    /// treating any Refunded payment as contributing zero.
    /// </summary>
    public decimal? RefundedAmount { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
