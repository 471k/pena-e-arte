using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Fallback <see cref="IPaymentProvider"/> for a studio with no connected POK account
/// (<see cref="Domain.Entities.Studio.PokMerchantId"/> is null / no <c>StudioCredentialRef</c>
/// row). Fails closed: every operation throws, so a card flow cannot silently succeed with no
/// real provider behind it. Capabilities are all-false so any capability-gated UI/business logic
/// treats card payments as unavailable rather than assuming support. Kept minimal — it exists to
/// keep the app booting and the test suite/seeder compiling, and as <c>PokPaymentProvider</c>'s
/// documented behaviour when a studio hasn't connected POK yet (see ADR-0001).
/// </summary>
public sealed class NullPaymentProvider : IPaymentProvider
{
    public PaymentProviderCapabilities Capabilities { get; } =
        new(SupportsSplit: false, SupportsAuthCapture: false, SupportsHoldExpiry: false,
            SupportedCurrencies: []);

    private static InvalidOperationException NotConfigured() =>
        new("No payment provider is configured. Flow A card payments are unavailable until the "
            + "POK provider is wired in (see ADR-0001). This is the expected state post-refactor.");

    public Task<(string ProviderReferenceId, string ClientToken)> CreatePaymentHoldAsync(
        PaymentHoldRequest request, CancellationToken ct) => throw NotConfigured();

    public Task CaptureAsync(Guid studioId, string providerReferenceId, CancellationToken ct) => throw NotConfigured();

    public Task CancelAsync(Guid studioId, string providerReferenceId, CancellationToken ct) => throw NotConfigured();

    public Task<PaymentProviderStatus?> GetStatusAsync(Guid studioId, string providerReferenceId, CancellationToken ct) => throw NotConfigured();

    public Task<string> RefundAsync(Guid studioId, string providerReferenceId, long? amountInCents, CancellationToken ct) => throw NotConfigured();
}
