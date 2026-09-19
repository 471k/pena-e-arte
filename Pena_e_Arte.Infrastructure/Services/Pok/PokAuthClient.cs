using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services.Pok;

/// <summary>
/// Everything every POK REST call needs before it can run: this studio's merchant id, a valid
/// bearer token, and an HttpClient — plus the shared response-handling/error-mapping logic.
/// Extracted from PokPaymentProvider (which owned all of this exclusively until
/// PokCardTokenService needed the identical credential-resolution/token-caching/error-handling
/// behavior for the card-tokenization endpoints) so neither class duplicates it. Behavior is
/// unchanged from the original PokPaymentProvider — this is a pure extraction, not a rewrite.
/// </summary>
public sealed class PokAuthClient(
    IAppDbContext db, ISecretsProvider secrets, IOptions<PokOptions> options,
    IHttpClientFactory httpClientFactory, IConnectionMultiplexer redis, ILogger<PokAuthClient> logger)
{
    private readonly PokOptions _options = options.Value;

    public string BaseUrl => _options.BaseUrl;

    /// <summary>"staging" whenever the configured host mentions it, "production" otherwise — the
    /// single source of truth for which POK environment is live; both PokPaymentProvider's
    /// Capabilities.Environment and PokCardTokenService derive from this same value.</summary>
    public string Environment =>
        _options.BaseUrl.Contains("staging", StringComparison.OrdinalIgnoreCase) ? "staging" : "production";

    /// <summary>This API's own publicly reachable base URL, used to build the webhookUrl POK
    /// POSTs to on order events. See PokOptions.WebhookCallbackBaseUrl.</summary>
    public string? WebhookCallbackBaseUrl => _options.WebhookCallbackBaseUrl;

    /// <summary>
    /// Every IPaymentProvider/card-tokenization method needs the same three things before it can
    /// call POK: this studio's merchant id, a valid bearer token, and an HttpClient.
    /// </summary>
    public async Task<(string MerchantId, string Token, HttpClient Http)> PrepareRequestAsync(Guid studioId, CancellationToken ct)
    {
        (string keyId, string keySecret, string merchantId) = await ResolveCredentialsAsync(studioId, ct);
        string token = await GetAccessTokenAsync(studioId, keyId, keySecret, ct);
        HttpClient http = httpClientFactory.CreateClient("Pok");
        return (merchantId, token, http);
    }

    public async Task<T> SendAsync<T>(
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

    // --- Wire DTOs -----------------------------------------------------------------------

    private sealed class LoginResponse
    {
        [JsonPropertyName("data")] public LoginResponseData? Data { get; init; }
    }

    private sealed class LoginResponseData
    {
        [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
        [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
    }
}
