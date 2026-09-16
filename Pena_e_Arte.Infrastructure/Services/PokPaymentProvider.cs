using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.Pok;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// IPaymentProvider for POK (RPAY SH.P.K.), Flow A's client-to-studio card provider (ADR-0001,
/// docs/payments/pok-assessment.md). Plain HttpClient + JWT Bearer, per-tenant credentials —
/// credential resolution/token caching/response handling live in PokAuthClient (shared with
/// PokCardTokenService, which needs the identical per-studio auth for the card-tokenization
/// endpoints).
///
/// <b>Two things verified only against docs, not a real sandbox call yet</b> (no staging
/// credentials existed when this was written — see the calling code's TODOs and the PR
/// description this shipped in for the concrete verification checklist):
/// 1. Whether POK's `amount` field is the currency's minor unit (matching this app's existing
///    "AmountInCents" convention, used for Stripe/EUR) or the whole-unit decimal amount. This
///    provider assumes minor-unit-in, whole-unit-out (divides by 100) — see AmountInCentsToPok.
/// 2. The exact sdkOrder flag combination for "authorized but not yet captured" — POK's docs
///    don't show that state explicitly. See MapStatus.
/// Both must be confirmed with a real staging transaction before this goes anywhere near
/// production traffic.
/// </summary>
public sealed class PokPaymentProvider(PokAuthClient authClient) : IPaymentProvider
{
    public PaymentProviderCapabilities Capabilities { get; } = new(
        SupportsSplit: true,
        SupportsAuthCapture: true,
        SupportsHoldExpiry: true,
        // Every documented example uses ALL or EUR. POK's card brands (Visa/Visa Electron/
        // Mastercard/Maestro) aren't currency-restricted per se, but only these two are
        // ever shown as a currencyCode — treat as the supported set until POK documents more.
        SupportedCurrencies: ["ALL", "EUR"],
        // "staging" whenever the configured host mentions it, "production" otherwise — this
        // is the single source of truth for which POK environment is live; the frontend reads
        // it from here (via PaymentCapabilitiesResponse) instead of guessing independently
        // from its own build mode, which can drift out of sync with this backend's actual host.
        Environment: authClient.Environment);

    public async Task<(string ProviderReferenceId, string ClientToken)> CreatePaymentHoldAsync(
        PaymentHoldRequest request, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(request.StudioId, ct);

        var body = new CreateOrderRequestBody
        {
            Amount = AmountInCentsToPok(request.AmountInCents),
            CurrencyCode = request.Currency,
            AutoCapture = false,
            ExpiresAfterMinutes = Capabilities.SupportsHoldExpiry ? request.HoldDurationMinutes : null,
            MerchantCustomReference = request.PaymentId.ToString(),
            WebhookUrl = string.IsNullOrEmpty(authClient.WebhookCallbackBaseUrl)
                ? null
                : $"{authClient.WebhookCallbackBaseUrl.TrimEnd('/')}/api/v1/webhooks/pok",
            SplitWith = request.PlatformFeeAmountInCents > 0 && !string.IsNullOrEmpty(authClient.PlatformMerchantId)
                ? new SplitWithBody
                {
                    MerchantId = authClient.PlatformMerchantId,
                    Amount = AmountInCentsToPok(request.PlatformFeeAmountInCents)
                }
                : null
        };

        using HttpRequestMessage req = new(HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);

        CreateOrderResponse result = await authClient.SendAsync<CreateOrderResponse>(http, req, request.StudioId, ct);
        string orderId = result.Data?.SdkOrder?.Id
            ?? throw new ServiceUnavailableException("POK did not return an order id.");

        // ProviderReferenceId and ClientToken are the same value for POK — there is no separate
        // client secret concept, only an order id the widget mounts against (see React docs).
        return (orderId, orderId);
    }

    public async Task CaptureAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        // Capture requires the amount in its body — fetch the order first rather than widen the
        // interface with an amount CaptureAsync doesn't otherwise need (we always capture in full;
        // partial capture isn't a feature CaptureDepositCommand exposes).
        SdkOrderDto order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct)
            ?? throw new NotFoundException("PokOrder", providerReferenceId);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/capture");
        req.Headers.Authorization = new("Bearer", token);
        // FinalAmount (amount + shippingCost per POK's docs), not Amount — the provider's
        // authoritative total to capture. We never send shippingCost today so the two are
        // identical in practice, but FinalAmount is the field that stays correct if that changes.
        req.Content = JsonContent.Create(new { amount = order.FinalAmount });

        await authClient.SendAsync<CreateOrderResponse>(http, req, studioId, ct, allow409: true);
    }

    public async Task CancelAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/cancel");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new { cancellationReason = "Hold expired or superseded" });

        await authClient.SendAsync<CreateOrderResponse>(http, req, studioId, ct, allow404: true);
    }

    public async Task<PaymentProviderStatus?> GetStatusAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        SdkOrderDto? order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct);
        return order is null ? null : MapStatus(order);
    }

    public async Task<string> RefundAsync(Guid studioId, string providerReferenceId, long? amountInCents, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/refund");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new
        {
            refundReason = "Refunded by studio",
            refundAmount = amountInCents.HasValue ? AmountInCentsToPok(amountInCents.Value) : (decimal?)null
        });

        await authClient.SendAsync<CreateOrderResponse>(http, req, studioId, ct);
        // POK's refund response echoes the sdkOrder, not a distinct refund id — the order id
        // itself is the only stable handle available for this operation.
        return providerReferenceId;
    }

    private async Task<SdkOrderDto?> GetOrderAsync(
        HttpClient http, string token, string merchantId, string sdkOrderId, Guid studioId, CancellationToken ct)
    {
        using HttpRequestMessage req = new(
            HttpMethod.Get, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{sdkOrderId}");
        req.Headers.Authorization = new("Bearer", token);

        GetOrderResponse? result = await authClient.SendAsync<GetOrderResponse>(http, req, studioId, ct, allow404: true);
        return result?.Data?.SdkOrder;
    }

    /// <summary>
    /// Maps POK's documented sdkOrder flags onto the normalized enum. POK does not document a
    /// distinct "authorized, not yet captured" flag — canBeCaptured is inferred from the one
    /// example that shows it (a completed autoCapture:true order has canBeCaptured:false), so an
    /// order that is not completed/canceled/refunded but reports canBeCaptured:true is treated as
    /// Authorized. Verify this against a real autoCapture:false staging order before trusting it
    /// for anything beyond the reconciliation job's own safety net.
    /// </summary>
    private static PaymentProviderStatus MapStatus(SdkOrderDto order) => order switch
    {
        { IsRefunded: true } => PaymentProviderStatus.Refunded,
        { IsCanceled: true } => PaymentProviderStatus.Canceled,
        { IsCompleted: true } => PaymentProviderStatus.Captured,
        { CanBeCaptured: true } => PaymentProviderStatus.Authorized,
        _ => PaymentProviderStatus.Pending,
    };

    /// <summary>
    /// This app's AmountInCents convention (Stripe-era) is minor-unit integers (100 = "1.00" of
    /// the currency). POK's own examples never show a fractional amount, which reads as
    /// whole-unit decimals, not minor units — so this divides by 100. THIS IS THE SINGLE
    /// HIGHEST-RISK UNVERIFIED ASSUMPTION IN THIS PROVIDER. A wrong guess here is a 100x
    /// over/undercharge. Confirm with one real staging transaction before production use.
    /// </summary>
    private static decimal AmountInCentsToPok(long amountInCents) => Math.Round(amountInCents / 100m, 2);

    // --- Wire DTOs -----------------------------------------------------------------------

    private sealed class CreateOrderRequestBody
    {
        [JsonPropertyName("amount")] public decimal Amount { get; init; }
        [JsonPropertyName("currencyCode")] public string CurrencyCode { get; init; } = "";
        [JsonPropertyName("autoCapture")] public bool AutoCapture { get; init; }
        [JsonPropertyName("expiresAfterMinutes")] public int? ExpiresAfterMinutes { get; init; }
        [JsonPropertyName("merchantCustomReference")] public string? MerchantCustomReference { get; init; }
        [JsonPropertyName("webhookUrl")] public string? WebhookUrl { get; init; }
        [JsonPropertyName("splitWith")] public SplitWithBody? SplitWith { get; init; }
    }

    private sealed class SplitWithBody
    {
        [JsonPropertyName("merchantId")] public string? MerchantId { get; init; }
        [JsonPropertyName("amount")] public decimal Amount { get; init; }
    }

    private sealed class CreateOrderResponse
    {
        [JsonPropertyName("data")] public CreateOrderResponseData? Data { get; init; }
    }

    private sealed class CreateOrderResponseData
    {
        [JsonPropertyName("sdkOrder")] public SdkOrderDto? SdkOrder { get; init; }
    }

    private sealed class GetOrderResponse
    {
        [JsonPropertyName("data")] public CreateOrderResponseData? Data { get; init; }
    }

    private sealed class SdkOrderDto
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("amount")] public decimal Amount { get; init; }
        [JsonPropertyName("finalAmount")] public decimal FinalAmount { get; init; }
        [JsonPropertyName("isCompleted")] public bool IsCompleted { get; init; }
        [JsonPropertyName("isCanceled")] public bool IsCanceled { get; init; }
        [JsonPropertyName("isRefunded")] public bool IsRefunded { get; init; }
        [JsonPropertyName("canBeCaptured")] public bool CanBeCaptured { get; init; }
    }
}
