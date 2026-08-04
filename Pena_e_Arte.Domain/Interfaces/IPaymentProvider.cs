namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// What a payment provider can do. Business/UI logic gates on these capabilities rather than
/// silently degrading to a lowest-common-denominator behaviour across providers.
/// </summary>
public sealed record PaymentProviderCapabilities(
    bool SupportsSplit,
    bool SupportsAuthCapture,
    bool SupportsHoldExpiry,
    IReadOnlyCollection<string> SupportedCurrencies);

/// <summary>
/// Provider-neutral payment operations for Flow A (client → studio). Replaces the deleted,
/// Stripe-aggregator-specific <c>IStripePaymentService</c> (which routed every charge through the
/// platform's own Stripe account — the Article 4(g) exposure Amendment A required deleted, not
/// migrated). The concrete provider (POK) is a separate, later ticket; until it lands the DI
/// default is <c>NullPaymentProvider</c>, which fails closed.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>What this provider supports — gate business/UI logic on this, never assume.</summary>
    PaymentProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Creates an authorization hold (auth without capture) for the given amount. Returns the
    /// provider's own reference id (stored as <c>Payment.ProviderReferenceId</c>) and a client
    /// secret the front end uses to complete the payment.
    /// </summary>
    Task<(string ProviderReferenceId, string ClientSecret)> CreatePaymentHoldAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct);

    /// <summary>Captures a previously-authorized hold.</summary>
    Task CaptureAsync(string providerReferenceId, CancellationToken ct);

    /// <summary>Cancels/releases a hold that was never captured.</summary>
    Task CancelAsync(string providerReferenceId, CancellationToken ct);

    /// <summary>Returns the provider's current status string, or null if unknown/not found.</summary>
    Task<string?> GetStatusAsync(string providerReferenceId, CancellationToken ct);

    /// <summary>Refunds a captured payment (full when amountInCents is null). Returns the refund id.</summary>
    Task<string> RefundAsync(string providerReferenceId, long? amountInCents, CancellationToken ct);
}
