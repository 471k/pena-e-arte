using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// IPaymentProvider for POK (RPAY SH.P.K.), Flow A's client-to-studio card provider (ADR-0001,
/// docs/payments/pok-assessment.md). Plain HttpClient + JWT Bearer, per-tenant credentials.
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
public sealed class PokPaymentProvider : IPaymentProvider
{
    private readonly IAppDbContext db;
    private readonly ISecretsProvider secrets;
    private readonly PokOptions _options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConnectionMultiplexer redis;
    private readonly ILogger<PokPaymentProvider> logger;

    public PaymentProviderCapabilities Capabilities { get; }

    public PokPaymentProvider(
        IAppDbContext db, ISecretsProvider secrets, IOptions<PokOptions> options,
        IHttpClientFactory httpClientFactory, IConnectionMultiplexer redis, ILogger<PokPaymentProvider> logger)
    {
        this.db = db;
        this.secrets = secrets;
        _options = options.Value;
        this.httpClientFactory = httpClientFactory;
        this.redis = redis;
        this.logger = logger;

        // Capabilities.Environment needs _options, so it's computed here rather than as a field
        // initializer — everything else about Capabilities is static.
        Capabilities = new(
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
            Environment: _options.BaseUrl.Contains("staging", StringComparison.OrdinalIgnoreCase)
                ? "staging" : "production");
    }

    public async Task<(string ProviderReferenceId, string ClientToken)> CreatePaymentHoldAsync(
        PaymentHoldRequest request, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await PrepareRequestAsync(request.StudioId, ct);

        var body = new CreateOrderRequestBody
        {
            Amount = AmountInCentsToPok(request.AmountInCents),
            CurrencyCode = request.Currency,
            AutoCapture = false,
            ExpiresAfterMinutes = Capabilities.SupportsHoldExpiry ? request.HoldDurationMinutes : null,
            MerchantCustomReference = request.PaymentId.ToString(),
            WebhookUrl = string.IsNullOrEmpty(_options.WebhookCallbackBaseUrl)
                ? null
                : $"{_options.WebhookCallbackBaseUrl.TrimEnd('/')}/api/v1/webhooks/pok",
            SplitWith = request.PlatformFeeAmountInCents > 0 && !string.IsNullOrEmpty(_options.PlatformMerchantId)
                ? new SplitWithBody
                {
                    MerchantId = _options.PlatformMerchantId,
                    Amount = AmountInCentsToPok(request.PlatformFeeAmountInCents)
                }
                : null
        };

        using HttpRequestMessage req = new(HttpMethod.Post, $"{_options.BaseUrl}/merchants/{merchantId}/sdk-orders");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(body);

        CreateOrderResponse result = await SendAsync<CreateOrderResponse>(http, req, request.StudioId, ct);
        string orderId = result.Data?.SdkOrder?.Id
            ?? throw new ServiceUnavailableException("POK did not return an order id.");

        // ProviderReferenceId and ClientToken are the same value for POK — there is no separate
        // client secret concept, only an order id the widget mounts against (see React docs).
        return (orderId, orderId);
    }

    public async Task CaptureAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await PrepareRequestAsync(studioId, ct);

        // Capture requires the amount in its body — fetch the order first rather than widen the
        // interface with an amount CaptureAsync doesn't otherwise need (we always capture in full;
        // partial capture isn't a feature CaptureDepositCommand exposes).
        SdkOrderDto order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct)
            ?? throw new NotFoundException("PokOrder", providerReferenceId);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{_options.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/capture");
        req.Headers.Authorization = new("Bearer", token);
        // FinalAmount (amount + shippingCost per POK's docs), not Amount — the provider's
        // authoritative total to capture. We never send shippingCost today so the two are
        // identical in practice, but FinalAmount is the field that stays correct if that changes.
        req.Content = JsonContent.Create(new { amount = order.FinalAmount });

        await SendAsync<CreateOrderResponse>(http, req, studioId, ct, allow409: true);
    }

    public async Task CancelAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{_options.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/cancel");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new { cancellationReason = "Hold expired or superseded" });

        await SendAsync<CreateOrderResponse>(http, req, studioId, ct, allow404: true);
    }

    public async Task<PaymentProviderStatus?> GetStatusAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await PrepareRequestAsync(studioId, ct);

        SdkOrderDto? order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct);
        return order is null ? null : MapStatus(order);
    }

    public async Task<string> RefundAsync(Guid studioId, string providerReferenceId, long? amountInCents, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{_options.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/refund");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new
        {
            refundReason = "Refunded by studio",
            refundAmount = amountInCents.HasValue ? AmountInCentsToPok(amountInCents.Value) : (decimal?)null
        });

        await SendAsync<CreateOrderResponse>(http, req, studioId, ct);
        // POK's refund response echoes the sdkOrder, not a distinct refund id — the order id
        // itself is the only stable handle available for this operation.
        return providerReferenceId;
    }

    /// <summary>
    /// Every IPaymentProvider method needs the same three things before it can call POK:
    /// this studio's merchant id, a valid bearer token, and an HttpClient. Factored out so a
    /// future change to how any of those three is obtained only needs to happen once.
    /// </summary>
    private async Task<(string MerchantId, string Token, HttpClient Http)> PrepareRequestAsync(Guid studioId, CancellationToken ct)
    {
        (string keyId, string keySecret, string merchantId) = await ResolveCredentialsAsync(studioId, ct);
        string token = await GetAccessTokenAsync(studioId, keyId, keySecret, ct);
        HttpClient http = httpClientFactory.CreateClient("Pok");
        return (merchantId, token, http);
    }

    private async Task<SdkOrderDto?> GetOrderAsync(
        HttpClient http, string token, string merchantId, string sdkOrderId, Guid studioId, CancellationToken ct)
    {
        using HttpRequestMessage req = new(
            HttpMethod.Get, $"{_options.BaseUrl}/merchants/{merchantId}/sdk-orders/{sdkOrderId}");
        req.Headers.Authorization = new("Bearer", token);

        using HttpResponseMessage response = await http.SendAsync(req, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        EnsureSuccessOrThrow(response, studioId);
        GetOrderResponse? result = await response.Content.ReadFromJsonAsync<GetOrderResponse>(cancellationToken: ct);
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

    private async Task<(string KeyId, string KeySecret, string MerchantId)> ResolveCredentialsAsync(
        Guid studioId, CancellationToken ct)
    {
        // One query instead of two separate round trips against Studios/StudioCredentialRefs —
        // both rows are needed together on every single payment operation. The Vault reads below
        // stay separate calls, and their result stays uncached here (unlike the bearer token):
        // caching a raw keySecret in Redis would put a live credential outside Vault, which is a
        // bigger security tradeoff than the extra round trip is worth.
        var row = await db.Studios.IgnoreQueryFilters()
            .Where(s => s.Id == studioId)
            .Select(s => new
            {
                s.PokMerchantId,
                CredentialRef = db.StudioCredentialRefs.IgnoreQueryFilters()
                    .FirstOrDefault(c => c.StudioId == studioId && c.Provider == CredentialProvider.Pok)
            })
            .FirstOrDefaultAsync(ct);

        if (row?.PokMerchantId is null || row.CredentialRef is null)
        {
            throw new PaymentProviderNotConnectedException(
                "This studio has not connected a POK account yet. Connect POK in Payment Settings before taking card deposits.");
        }

        // Convention: StudioCredentialRef.SecretPath holds the Vault PATH only (one KV object per
        // studio+provider, two fields inside it) — not a full "path:field" key. GetSecretAsync's
        // ":field" suffix is appended here for each of the two fields it holds.
        //
        // A missing/unreadable secret here (Vault down, or the crash-consistency gap between
        // ConnectPokAccountCommand's DB commit and its Vault write — see that command's own
        // comment) surfaces the same actionable message as "never connected": ISecretsProvider
        // doesn't distinguish "unreachable" from "missing" (both throw InvalidOperationException),
        // and ExceptionMiddleware has no mapping for that type, which would otherwise bubble up as
        // a bare 500 instead of the 422 PAYMENT_PROVIDER_NOT_CONNECTED every other "not set up yet"
        // path in this class already returns.
        try
        {
            string keyId = await secrets.GetSecretAsync($"{row.CredentialRef.SecretPath}:keyId", ct);
            string keySecret = await secrets.GetSecretAsync($"{row.CredentialRef.SecretPath}:keySecret", ct);
            return (keyId, keySecret, row.PokMerchantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PaymentProviderNotConnectedException(
                "This studio's POK connection is incomplete or unreadable. Reconnect POK in Payment Settings.");
        }
    }

    /// <summary>
    /// Resolves a cached bearer token from Redis (never process memory — CLAUDE.md rule against
    /// in-memory state that belongs in Redis; a token must be valid across API replicas), or logs
    /// in and caches. Refreshes 60s before expiry. A short Redis lock keeps concurrent requests
    /// for the same studio from all racing POK's login endpoint at once — best-effort, not a full
    /// distributed lock: a losing waiter that times out logs in itself rather than hanging.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(Guid studioId, string keyId, string keySecret, CancellationToken ct)
    {
        IDatabase cache = redis.GetDatabase();
        string cacheKey = $"pok:token:{studioId}";

        string? cached = await TryRedisGetAsync(cache, cacheKey);
        if (cached is not null)
            return cached;

        string lockKey = $"pok:token-lock:{studioId}";
        bool acquired = await TryRedisAcquireLockAsync(cache, lockKey, TimeSpan.FromSeconds(15));

        if (!acquired)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(200, ct);
                cached = await TryRedisGetAsync(cache, cacheKey);
                if (cached is not null)
                    return cached;
            }
            logger.LogWarning("Timed out waiting for another request's POK token refresh for studio {StudioId}; logging in independently.", studioId);
        }

        try
        {
            cached = await TryRedisGetAsync(cache, cacheKey);
            if (cached is not null)
                return cached;

            (string token, TimeSpan ttl) = await LoginAsync(keyId, keySecret, studioId, ct);
            // Refresh 60s early so a request never hands out a token that expires mid-flight.
            TimeSpan cacheTtl = ttl > TimeSpan.FromSeconds(90) ? ttl - TimeSpan.FromSeconds(60) : ttl;
            await TryRedisSetAsync(cache, cacheKey, token, cacheTtl);
            return token;
        }
        finally
        {
            if (acquired)
                await TryRedisDeleteAsync(cache, lockKey);
        }
    }

    // Redis here is a best-effort cache/lock, not the source of truth for a POK login — same
    // fail-open convention as SlotLocker.cs. A Redis outage must degrade to "log in every call"
    // (slower, extra POK auth traffic), never to a hard failure of every payment operation.
    private static async Task<string?> TryRedisGetAsync(IDatabase cache, string key)
    {
        try { return await cache.StringGetAsync(key); }
        catch (RedisConnectionException) { return null; }
    }

    private static async Task TryRedisSetAsync(IDatabase cache, string key, string value, TimeSpan ttl)
    {
        try { await cache.StringSetAsync(key, value, ttl); }
        catch (RedisConnectionException) { /* best-effort cache write; a miss just means re-login next call */ }
    }

    private static async Task TryRedisDeleteAsync(IDatabase cache, string key)
    {
        try { await cache.KeyDeleteAsync(key); }
        catch (RedisConnectionException) { /* the lock key expires on its own TTL */ }
    }

    private static async Task<bool> TryRedisAcquireLockAsync(IDatabase cache, string key, TimeSpan ttl)
    {
        try { return await cache.StringSetAsync(key, "1", ttl, When.NotExists); }
        catch (RedisConnectionException) { return true; } // fail-open: skip contention handling entirely
    }

    private async Task<(string Token, TimeSpan Ttl)> LoginAsync(string keyId, string keySecret, Guid studioId, CancellationToken ct)
    {
        HttpClient http = httpClientFactory.CreateClient("Pok");
        using HttpRequestMessage req = new(HttpMethod.Post, $"{_options.BaseUrl}/auth/sdk/login")
        {
            Content = JsonContent.Create(new { keyId, keySecret })
        };

        LoginResponse result = await SendAsync<LoginResponse>(http, req, studioId, ct);
        LoginResponseData data = result.Data
            ?? throw new ServiceUnavailableException("POK login returned no data.");

        // expiresIn's units are contradictory across POK's own docs (seconds in one place,
        // milliseconds-as-a-string in another) — expiresAt is the one unambiguous field, use it.
        TimeSpan ttl = data.ExpiresAt is { } expiresAt && expiresAt > DateTimeOffset.UtcNow
            ? expiresAt - DateTimeOffset.UtcNow
            : TimeSpan.FromMinutes(10); // conservative fallback if expiresAt is ever absent

        return (data.AccessToken ?? throw new ServiceUnavailableException("POK login returned no access token."), ttl);
    }

    private async Task<T> SendAsync<T>(
        HttpClient http, HttpRequestMessage req, Guid studioId, CancellationToken ct,
        bool allow404 = false, bool allow409 = false)
    {
        using HttpResponseMessage response = await http.SendAsync(req, ct);

        if (allow404 && response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return default!;
        // 409 on capture means "already captured" per POK's docs — treat as success, not an error.
        if (allow409 && response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return default!;

        EnsureSuccessOrThrow(response, studioId);
        T? result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return result ?? throw new ServiceUnavailableException("POK returned an empty response.");
    }

    private void EnsureSuccessOrThrow(HttpResponseMessage response, Guid studioId)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Never log response bodies here — POK's error payloads can include billing/card details
        // (CLAUDE.md rule 3, no PII in logs). Status code + studio id only.
        logger.LogError("POK request failed for studio {StudioId}: {StatusCode}", studioId, response.StatusCode);
        throw new ServiceUnavailableException($"POK request failed ({(int)response.StatusCode}).");
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
        [JsonPropertyName("splitWith")] public SplitWithBody? SplitWith { get; init; }
    }

    private sealed class SplitWithBody
    {
        [JsonPropertyName("merchantId")] public string? MerchantId { get; init; }
        [JsonPropertyName("amount")] public decimal Amount { get; init; }
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("data")] public LoginResponseData? Data { get; init; }
    }

    private sealed class LoginResponseData
    {
        [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
        [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
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
