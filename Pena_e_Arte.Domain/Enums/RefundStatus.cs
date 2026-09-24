namespace Pena_e_Arte.Domain.Enums;

/// <summary>Lifecycle of a SubscriptionRefund's underlying Stripe refund (or the local-only
/// record of an admin's explicit "no refund" decision).</summary>
public enum RefundStatus
{
    Pending,
    Succeeded,
    Failed
}
