using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// What a payment provider can do. Business/UI logic gates on these capabilities rather than
/// silently degrading to a lowest-common-denominator behaviour across providers.
/// </summary>
public sealed record PaymentProviderCapabilities(
    bool SupportsSplit,
    bool SupportsAuthCapture,
    bool SupportsHoldExpiry,
    IReadOnlyCollection<string> SupportedCurrencies,
    /// <summary>Which environment this provider is actually configured against (e.g. "staging" /
    /// "production" for POK), or null for a provider with no such concept. The client-side widget
    /// must target the same environment as the backend it's talking to, or POK 401s — so the
    /// frontend reads this from the backend rather than guessing independently from its own build
    /// mode, which can drift out of sync with the backend's actual configured host.</summary>
    string? Environment = null);

/// <summary>
/// Everything a provider needs to open a hold. A request object, not a growing positional
/// parameter list, because provider needs differ (POK needs StudioId to resolve per-tenant
/// credentials and route the URL; a platform-wide provider would ignore it) — see ADR-0001
/// "Consequences for the codebase" #2.
/// </summary>
public sealed record PaymentHoldRequest(
    Guid StudioId,
    Guid PaymentId,
    long AmountInCents,
    string Currency,
    /// <summary>Platform fee taken atomically at payment time (ADR-0001 monetization, wired
    /// at 0% from day one). Providers without SupportsSplit must ignore this.</summary>
    long PlatformFeeAmountInCents = 0,
    /// <summary>How long the hold stays payable before it self-expires. Only meaningful when
    /// Capabilities.SupportsHoldExpiry is true; a provider without it ignores this and the
    /// caller must not rely on server-side expiry.</summary>
    int? HoldDurationMinutes = null);

/// <summary>
/// Provider-neutral payment operations for Flow A (client → studio). Replaces the deleted,
/// Stripe-aggregator-specific <c>IStripePaymentService</c> (which routed every charge through the
/// platform's own Stripe account — the Article 4(g) exposure Amendment A required deleted, not
/// migrated). Every method takes <c>studioId</c> explicitly rather than resolving it from ambient
/// tenant context: <c>PaymentReconciliationJob</c> runs cross-tenant with
/// <c>IgnoreQueryFilters()</c>, so there is no ambient tenant to resolve from, and POK needs the
/// studio to resolve per-tenant credentials (ADR-0001 — each studio issues its own keyId/keySecret,
/// there is no platform-level key).
/// </summary>
public interface IPaymentProvider
{
    /// <summary>What this provider supports — gate business/UI logic on this, never assume.</summary>
    PaymentProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Creates an authorization hold (auth without capture) for the given amount. Returns the
    /// provider's own reference id (stored as <c>Payment.ProviderReferenceId</c>) and a
    /// client-facing token the front end uses to complete the payment — a Stripe PaymentIntent
    /// client secret, or a POK sdkOrder id. Not necessarily a cryptographic secret, which is why
    /// it's persisted into <c>Payment.ClientToken</c> rather than a field named ClientSecret.
    /// </summary>
    Task<(string ProviderReferenceId, string ClientToken)> CreatePaymentHoldAsync(
        PaymentHoldRequest request, CancellationToken ct);

    /// <summary>Captures a previously-authorized hold.</summary>
    Task CaptureAsync(Guid studioId, string providerReferenceId, CancellationToken ct);

    /// <summary>Cancels/releases a hold that was never captured.</summary>
    Task CancelAsync(Guid studioId, string providerReferenceId, CancellationToken ct);

    /// <summary>Returns the payment's normalized status, or null if unknown/not found.</summary>
    Task<PaymentProviderStatus?> GetStatusAsync(Guid studioId, string providerReferenceId, CancellationToken ct);

    /// <summary>Refunds a captured payment (full when amountInCents is null). Returns the refund id.</summary>
    Task<string> RefundAsync(Guid studioId, string providerReferenceId, long? amountInCents, CancellationToken ct);
}
