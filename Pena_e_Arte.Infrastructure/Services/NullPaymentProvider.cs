using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Placeholder <see cref="IPaymentProvider"/> registered as the DI default until the real POK
/// provider lands (separate ticket). Fails closed: every operation throws, so a card flow cannot
/// silently succeed with no real provider behind it. Capabilities are all-false so any
/// capability-gated UI/business logic treats card payments as unavailable rather than assuming
/// support. Kept minimal — it exists to keep the app booting and the test suite/seeder compiling.
/// </summary>
public sealed class NullPaymentProvider : IPaymentProvider
{
    public PaymentProviderCapabilities Capabilities { get; } =
        new(SupportsSplit: false, SupportsAuthCapture: false, SupportsHoldExpiry: false,
            SupportedCurrencies: []);

    private static InvalidOperationException NotConfigured() =>
        new("No payment provider is configured. Flow A card payments are unavailable until the "
            + "POK provider is wired in (see ADR-0001). This is the expected state post-refactor.");

    public Task<(string ProviderReferenceId, string ClientSecret)> CreatePaymentHoldAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct) => throw NotConfigured();

    public Task CaptureAsync(string providerReferenceId, CancellationToken ct) => throw NotConfigured();

    public Task CancelAsync(string providerReferenceId, CancellationToken ct) => throw NotConfigured();

    public Task<string?> GetStatusAsync(string providerReferenceId, CancellationToken ct) => throw NotConfigured();

    public Task<string> RefundAsync(string providerReferenceId, long? amountInCents, CancellationToken ct) => throw NotConfigured();
}
