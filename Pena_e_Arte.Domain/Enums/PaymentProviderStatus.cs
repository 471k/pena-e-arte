namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// Normalized status IPaymentProvider.GetStatusAsync returns. Each provider maps its own
/// vocabulary onto this set internally (Stripe's "requires_capture"/"succeeded", POK's
/// sdkOrder.isCompleted/isCanceled/isRefunded flags) so callers never branch on a
/// provider-specific string — the coupling that made the original Stripe-only handlers
/// break the moment a second provider (POK) needed to answer the same question.
/// </summary>
public enum PaymentProviderStatus
{
    /// <summary>Created, awaiting the client to complete the payment step.</summary>
    Pending,

    /// <summary>Authorized/held, not yet captured.</summary>
    Authorized,

    /// <summary>Captured — funds moved.</summary>
    Captured,

    /// <summary>Canceled/released before capture.</summary>
    Canceled,

    /// <summary>Refunded (in full — providers here have no partial-refund status,
    /// Payment.RefundedAmount tracks the amount separately).</summary>
    Refunded,

    /// <summary>Failed — declined, expired, or otherwise dead.</summary>
    Failed,
}
