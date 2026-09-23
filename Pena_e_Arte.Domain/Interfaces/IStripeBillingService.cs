namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Outcome of a Stripe Checkout subscription session, read back when activating.
/// <paramref name="ClientReferenceId"/> carries the studio id set at session creation.
/// </summary>
public record CheckoutSubscriptionResult(
    bool IsComplete,
    string? StripeSubscriptionId,
    string? StripeCustomerId,
    string? ClientReferenceId,
    string? PriceId,
    DateTime CurrentPeriodEnd,
    bool HasDiscount);

public interface IStripeBillingService
{
    Task<string> CreateCustomerAsync(string email, CancellationToken ct);

    /// <summary>
    /// Creates a Stripe-hosted Checkout Session (mode=subscription) that collects the
    /// owner's card and creates the subscription on payment. Returns the redirect URL.
    /// When <paramref name="trialEnd"/> is set, the card is collected now but the first
    /// charge is deferred to that date — used to credit a cash period already paid for.
    /// </summary>
    Task<string> CreateSubscriptionCheckoutAsync(
        string customerId, string priceId, string clientReferenceId,
        string successUrl, string cancelUrl, string? couponId, DateTime? trialEnd, CancellationToken ct);

    /// <summary>
    /// Reads a Checkout Session back from Stripe to reconcile local state.
    /// Null when the session no longer exists.
    /// </summary>
    Task<CheckoutSubscriptionResult?> GetCheckoutSubscriptionAsync(string sessionId, CancellationToken ct);

    Task<(string SubscriptionId, DateTime CurrentPeriodEnd)> CreateSubscriptionAsync(
        string customerId, string priceId, string? couponId, CancellationToken ct);

    /// <summary>
    /// Switches the subscription to a new price immediately, invoicing the prorated
    /// difference right away. Used for upgrades. Returns the new period end.
    /// </summary>
    Task<DateTime> ChangeSubscriptionPriceAsync(
        string stripeSubscriptionId, string newPriceId, CancellationToken ct);

    /// <summary>
    /// Schedules a price change to take effect at the end of the current billing period.
    /// Used for downgrades — the studio keeps what it already paid for.
    /// <paramref name="newPriceInterval"/> is the new price's billing interval ("month" or "year").
    /// </summary>
    Task ScheduleSubscriptionPriceChangeAsync(
        string stripeSubscriptionId, string currentPriceId, string newPriceId,
        string newPriceInterval, CancellationToken ct);

    /// <summary>Cancels a previously scheduled price change. Idempotent.</summary>
    Task CancelScheduledPriceChangeAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>
    /// Creates a Stripe-hosted Customer Portal session so the owner can manage their
    /// payment method, download invoices, and cancel. Returns the redirect URL.
    /// </summary>
    Task<string> CreatePortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct);

    /// <summary>
    /// Cancels an active Stripe subscription immediately. Idempotent — safe to call if the
    /// subscription is already cancelled or does not exist. Callers should catch exceptions
    /// and log rather than rethrow — Stripe failure must not abort a local cancellation.
    /// </summary>
    Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>
    /// Applies a Stripe coupon to an already-active subscription. Used to reward the
    /// referring studio when their referral code converts a new paying studio. The coupon
    /// is applied as a discount on the subscription's next invoice. Used for a Monthly
    /// referrer subscription — see CreditCustomerBalanceAsync for the Yearly case.
    /// </summary>
    Task ApplyCouponToActiveSubscriptionAsync(
        string stripeSubscriptionId, string couponId, CancellationToken ct);

    /// <summary>
    /// Credits the subscription's customer balance (applied by Stripe to the next
    /// invoice). Used to reward a Yearly-billed referrer — a coupon would either expire
    /// unused (repeating, 1 month, but the next invoice is up to a year away) or zero a
    /// whole renewal (percent-off), so a balance credit worth one month is used instead.
    /// Returns the customer balance transaction id.
    /// </summary>
    Task<string> CreditCustomerBalanceAsync(
        string stripeSubscriptionId, decimal amount, string idempotencyKey, string description, CancellationToken ct);
}
