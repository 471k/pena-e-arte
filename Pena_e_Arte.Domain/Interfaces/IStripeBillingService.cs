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

/// <summary>The Stripe-side facts G3 checks a linked PlanPrice against.</summary>
public record StripePriceInfo(
    bool Active, long? UnitAmount, string Currency, string? RecurringInterval, long? IntervalCount);

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

    /// <summary>Reads a price back from Stripe for G3's amount/interval match check. Null when not found.</summary>
    Task<StripePriceInfo?> GetPriceAsync(string stripePriceId, CancellationToken ct);

    /// <summary>Reads what a Stripe subscription is currently billed (its first item's price)
    /// — used by the billed-amount backfill (R6) to snapshot subscriptions created before
    /// Batch 2b. Null when the subscription has no items.</summary>
    Task<StripePriceInfo?> GetSubscriptionBilledPriceAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>The PaymentIntent that paid an invoice (the target of a refund), or null when it can't
    /// be resolved. Fetches the invoice with <c>expand[]=payments</c> because Invoice.payments is an
    /// expandable field: Stripe omits it from webhook payloads and from a plain retrieve (verified
    /// against API version 2026-05-27.dahlia in a real test-mode run, 2026-09-24).</summary>
    Task<string?> GetInvoicePaymentIntentIdAsync(string stripeInvoiceId, CancellationToken ct);

    /// <summary>
    /// D6: pauses collection on an active Stripe subscription — renewals during the pause
    /// generate no invoice/charge. The period already paid is not refunded. Idempotent.
    /// </summary>
    Task PauseCollectionAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>D6: clears a paused subscription's pause_collection — billing resumes at the
    /// next normal renewal date. Idempotent.</summary>
    Task ResumeCollectionAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>Sets cancel_at_period_end=true — the studio keeps access to CurrentPeriodEnd,
    /// same as today's monthly-cancel-via-portal behaviour. Idempotent.</summary>
    Task ScheduleCancellationAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>Clears cancel_at_period_end — "Keep my plan". Idempotent.</summary>
    Task UndoScheduledCancellationAsync(string stripeSubscriptionId, CancellationToken ct);

    /// <summary>Refunds part or all of a payment (the yearly-refund formula's computed amount,
    /// or an admin override). amountInCents in integer cents. Idempotent via idempotencyKey —
    /// callers use subscription id + invoice period start so a retry never double-refunds.
    /// Returns the Stripe refund id and its initial status ("succeeded"/"pending"/"failed").</summary>
    Task<(string RefundId, string Status)> RefundAsync(
        string stripePaymentIntentId, long amountInCents, string idempotencyKey, CancellationToken ct);
}
