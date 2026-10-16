using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;
using Pena_e_Arte.Infrastructure.Services.Pok;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// IPaymentProvider for POK (RPAY SH.P.K.), Flow A's client-to-studio card provider (ADR-0001,
/// docs/payments/pok-assessment.md). Plain HttpClient + JWT Bearer, per-tenant credentials —
/// credential resolution/token caching/response handling live in PokAuthClient (shared with
/// PokCardTokenService, which needs the identical per-studio auth for the card-tokenization
/// endpoints).
///
/// <b>Verified against POK staging on 2026-10-14</b> (release gate; evidence in
/// docs/payments/runbook-studio-currency-migration-2026-09-27.md, "§3.1 result"):
/// 1. Amount unit: POK takes whole-unit decimals, exactly what ToPokAmount sends — confirmed for
///    ALL (create 1234, full capture 1234, partial capture 617, partial refund 234) and EUR
///    (create 12.34, capture 12.34, refund 2.34), with no currency conversion. ToPokAmount is
///    unchanged.
/// 2. Held state (autoCapture:false, after 3-D Secure, before capture): isConfirmed:true,
///    canBeCaptured:true, isCaptured:false, isCompleted:false, capturedAmount:0, status
///    "awaiting_capture" — MapStatus maps it to Authorized (held-before-paid precedence, POK's
///    own dashboard calls it "On hold"). The pre-gate isCompleted-first precedence would not have
///    misfired on this shape, but stays guarded by a regression test.
/// 3. Hosted-page return (2026-10-16): redirectUrl / failRedirectUrl are honoured — see PaymentResultUrl.
/// Still open (docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md): how long a hold can be
/// captured after the order's 24 h expiresAt (C-4), and which environments point at POK
/// production (C-2). No studio is enabled for live card deposits until those are decided.
/// </summary>
public sealed class PokPaymentProvider(
    PokAuthClient authClient,
    IOptions<CardDepositOptions> cardDeposits,
    IAppSettings appSettings,
    ILogger<PokPaymentProvider> logger) : IPaymentProvider
{
    /// <summary>Frontend page a payer lands on after paying on POK's own hosted page (G-12).</summary>
    public const string PaymentResultPath = "/payment-result";

    public PaymentProviderCapabilities Capabilities { get; } = new(
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
        Environment: authClient.Environment,
        // Go-live kill switch (C-13): off unless Payments:CardDepositsEnabled is true.
        CardPaymentsEnabled: cardDeposits.Value.CardDepositsEnabled);

    public async Task<(string ProviderReferenceId, string ClientToken)> CreatePaymentHoldAsync(
        PaymentHoldRequest request, CancellationToken ct)
    {
        // Defence in depth behind CardCurrencyGuard, like the currency check below.
        if (!Capabilities.CardPaymentsEnabled)
        {
            throw new BusinessRuleViolationException(CardCurrencyGuard.CardPaymentsSwitchedOffMessage);
        }

        // Defence in depth behind CardCurrencyGuard (which every caller already runs before
        // reaching a provider at all) — this provider must never accept a currency it can't
        // actually charge in, even if a future caller forgets the guard.
        if (!Capabilities.SupportedCurrencies.Contains(request.Currency, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessRuleViolationException(
                $"Card payments aren't available in {request.Currency} for this studio. Please pay in cash.");
        }

        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(request.StudioId, ct);

        var body = new CreateOrderRequestBody
        {
            Amount = ToPokAmount(request.Amount, request.Currency),
            CurrencyCode = request.Currency,
            AutoCapture = false,
            ExpiresAfterMinutes = Capabilities.SupportsHoldExpiry ? request.HoldDurationMinutes : null,
            MerchantCustomReference = request.PaymentId.ToString(),
            WebhookUrl = string.IsNullOrEmpty(authClient.WebhookCallbackBaseUrl)
                ? null
                : $"{authClient.WebhookCallbackBaseUrl.TrimEnd('/')}/api/v1/webhooks/pok",
            RedirectUrl = PaymentResultUrl("paid", request.PaymentId),
            FailRedirectUrl = PaymentResultUrl("failed", request.PaymentId),
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

        // Full capture: fetch the order for its authorised total. A partial capture (the late-cancel
        // policy, B3) uses the overload that takes an amount.
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

    public async Task CaptureAsync(Guid studioId, string providerReferenceId, decimal amount, string currency, CancellationToken ct)
    {
        if (amount <= 0m)
            throw new BusinessRuleViolationException("Capture amount must be greater than zero.");

        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/capture");
        req.Headers.Authorization = new("Bearer", token);
        // Same unit conversion as the hold itself — this is the second amount-unit-sensitive write
        // path the gate verifies (Phase 4, T5b).
        decimal pokAmount = ToPokAmount(amount, currency);
        req.Content = JsonContent.Create(new { amount = pokAmount });

        CreateOrderResponse? result = await authClient.SendAsync<CreateOrderResponse>(http, req, studioId, ct, allow409: true);
        if (result is not null)
            return;

        // 409 = POK says the order was already captured, possibly IN FULL ("Capture deposit",
        // Complete, No-show or the POK dashboard; POK allows one capture per order). Accept it
        // only as an idempotent retry of this same partial capture. Otherwise the caller would
        // record a released remainder that was in fact charged.
        SdkOrderDto? order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct);
        if (order?.CapturedAmount == pokAmount)
            return;

        string orderRef = providerReferenceId.Length > 8 ? providerReferenceId[..8] : providerReferenceId;
        logger.LogWarning(
            "POK order {OrderRef} for studio {StudioId} was already captured (capturedAmount={CapturedAmount}); refused a partial capture of {Amount}.",
            orderRef, studioId, order?.CapturedAmount, pokAmount);
        throw new ServiceUnavailableException("POK request failed (409): the order was already captured.");
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
        if (order is null)
            return null;

        PaymentProviderStatus mapped = MapStatus(order);
        // First 8 characters only: POK's unauthenticated public GET serves the whole order
        // (merchant contact details included) to anyone holding the full id.
        string orderRef = providerReferenceId.Length > 8 ? providerReferenceId[..8] : providerReferenceId;
        // Flags, amounts and POK's own status string only — never the raw body (it carries the
        // merchant's contact details and card/billing data; CLAUDE.md rule 3).
        logger.LogInformation(
            "POK order {OrderRef} for studio {StudioId}: confirmed={IsConfirmed} canBeCaptured={CanBeCaptured} captured={IsCaptured} completed={IsCompleted} canceled={IsCanceled} refunded={IsRefunded} amount={Amount} finalAmount={FinalAmount} capturedAmount={CapturedAmount} currency={Currency} pokStatus={PokStatus} mapped={Mapped}",
            orderRef, studioId, order.IsConfirmed, order.CanBeCaptured, order.IsCaptured,
            order.IsCompleted, order.IsCanceled, order.IsRefunded, order.Amount, order.FinalAmount,
            order.CapturedAmount, order.CurrencyCode, order.Status, mapped);

        if (mapped == PaymentProviderStatus.Failed)
            logger.LogWarning(
                "POK order {OrderRef} for studio {StudioId} is completed but neither capturable nor captured (pokStatus={PokStatus}); treating the hold as lapsed.",
                orderRef, studioId, order.Status);

        return mapped;
    }

    public async Task<string> RefundAsync(Guid studioId, string providerReferenceId, decimal? amount, string currency, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/refund");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new
        {
            refundReason = "Refunded by studio",
            refundAmount = amount.HasValue ? ToPokAmount(amount.Value, currency) : (decimal?)null
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
    /// Maps POK's sdkOrder flags onto the normalized enum. Held (isConfirmed && canBeCaptured) is
    /// checked BEFORE anything that reads as paid, as POK's own merchant dashboard does (verified
    /// 2026-10-07) — isCompleted alone is never treated as "money moved", because POK may set it on
    /// an authorized-but-uncaptured order. Captured requires positive evidence (isCaptured, or
    /// capturedAmount > 0). The created/expired/paid/partial-capture fixtures are real staging
    /// shapes; the held fixture is real once the release gate's T1 records it; isCompleted-only ->
    /// Failed is provisional until the gate's aging probe. See
    /// docs/payments/runbook-studio-currency-migration-2026-09-27.md §3.1.
    /// </summary>
    private static PaymentProviderStatus MapStatus(SdkOrderDto order) => order switch
    {
        { IsRefunded: true } => PaymentProviderStatus.Refunded,
        { IsCanceled: true } => PaymentProviderStatus.Canceled,
        // Held: still capturable. Treat a missing isConfirmed as true (field absent on older shapes).
        { CanBeCaptured: true, IsConfirmed: not false } => PaymentProviderStatus.Authorized,
        { IsCaptured: true } => PaymentProviderStatus.Captured,
        { CapturedAmount: > 0m } => PaymentProviderStatus.Captured,
        // Completed, but neither capturable nor captured: a lapsed or voided hold. Never read it as
        // money received. PROVISIONAL — Phase 7's aging probe records the real lapsed-hold shape.
        { IsCompleted: true } => PaymentProviderStatus.Failed,
        _ => PaymentProviderStatus.Pending,
    };

    /// <summary>
    /// Assumes POK takes whole-unit decimals in the currency's ISO minor unit (e.g. "50.00" for
    /// EUR, "5000" for ALL) — POK's own examples never show a fractional amount, which reads as
    /// whole-unit, not minor-unit. THIS IS THE SINGLE HIGHEST-RISK UNVERIFIED ASSUMPTION IN THIS
    /// PROVIDER; if the staging transaction (see the class doc comment) shows minor-unit integers
    /// instead, change only this method.
    /// </summary>
    private static decimal ToPokAmount(decimal amount, string currency) => CurrencyCatalog.Round(amount, currency);

    /// <summary>
    /// Where POK's hosted page sends the payer after its own result screen (G-12): without these,
    /// POK falls back to a default development URL that doesn't load. Verified on POK staging
    /// 2026-10-16: both fields are stored, POK appends "&amp;orderId=&lt;id&gt;" (so our URL always
    /// carries a query string), paid → redirectUrl, cancelled 3-D Secure → failRedirectUrl. The
    /// in-app card form (SDK) ignores them. Only https is sent — POK has only been seen with
    /// https, and a local http base must never break order creation.
    /// </summary>
    private string? PaymentResultUrl(string status, Guid paymentId)
    {
        if (!Uri.TryCreate(appSettings.BaseUrl, UriKind.Absolute, out Uri? baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            return null;
        return $"{appSettings.BaseUrl.TrimEnd('/')}{PaymentResultPath}?status={status}&payment={paymentId:D}";
    }

    // --- Wire DTOs -----------------------------------------------------------------------

    private sealed class CreateOrderRequestBody
    {
        [JsonPropertyName("amount")] public decimal Amount { get; init; }
        [JsonPropertyName("currencyCode")] public string CurrencyCode { get; init; } = "";
        [JsonPropertyName("autoCapture")] public bool AutoCapture { get; init; }
        [JsonPropertyName("expiresAfterMinutes")] public int? ExpiresAfterMinutes { get; init; }
        [JsonPropertyName("merchantCustomReference")] public string? MerchantCustomReference { get; init; }
        [JsonPropertyName("webhookUrl")] public string? WebhookUrl { get; init; }
        [JsonPropertyName("redirectUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RedirectUrl { get; init; }
        [JsonPropertyName("failRedirectUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FailRedirectUrl { get; init; }
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
        [JsonPropertyName("currencyCode")] public string? CurrencyCode { get; init; }
        // Undocumented by POK, but present on the merchant-scoped GET (verified 2026-10-07):
        [JsonPropertyName("isConfirmed")] public bool? IsConfirmed { get; init; }
        [JsonPropertyName("isCaptured")] public bool? IsCaptured { get; init; }
        [JsonPropertyName("capturedAmount")] public decimal? CapturedAmount { get; init; }
        // POK's own status string ("created", "expired", "paid", …) — diagnostics only, never branch on it.
        [JsonPropertyName("status")] public string? Status { get; init; }
    }
}
