namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Aggregator model: all PaymentIntents go directly to the platform's Stripe account.
/// No connected account headers.
/// </summary>
public interface IStripePaymentService
{
    Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct);

    Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>Cancels an unauthorized/uncaptured PaymentIntent (e.g. client switched to cash).</summary>
    Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>
    /// Current Stripe-side status of a PaymentIntent ("requires_payment_method",
    /// "requires_capture", "succeeded", "canceled", …) or null when it no longer exists.
    /// Used to reconcile local payment state when webhooks were missed.
    /// </summary>
    Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct);

    Task<string> RefundPaymentIntentAsync(
        string paymentIntentId, long? amountInCents, CancellationToken ct);
}
