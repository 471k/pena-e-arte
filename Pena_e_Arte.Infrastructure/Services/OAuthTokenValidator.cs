using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Validates Google and Apple ID tokens without any third-party SDK.
/// Uses IHttpClientFactory to fetch JWKS and IDistributedCache (Redis) to cache them.
/// JwtSecurityTokenHandler is from System.IdentityModel.Tokens.Jwt — already in the project.
/// </summary>
public sealed class OAuthTokenValidator(
    IHttpClientFactory httpFactory,
    IDistributedCache cache,
    IOptions<GoogleOptions> googleOpts,
    IOptions<AppleOptions> appleOpts,
    ILogger<OAuthTokenValidator> logger) : IOAuthTokenValidator
{
    private const string GoogleJwksUrl = "https://www.googleapis.com/oauth2/v3/certs";
    private const string GoogleIssuer = "https://accounts.google.com";
    private const string AppleJwksUrl = "https://appleid.apple.com/auth/keys";
    private const string AppleIssuer = "https://appleid.apple.com";

    private readonly string _googleAudience = googleOpts.Value.ClientId;
    private readonly string _appleAudience = appleOpts.Value.ClientId;

    public async Task<OAuthUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct)
    {
        JsonWebKeySet jwks = await GetJwksAsync("jwks_google", GoogleJwksUrl, ct);
        return ValidateAndExtract(idToken, GoogleIssuer, _googleAudience, jwks, "Google");
    }

    public async Task<OAuthUserInfo> ValidateAppleTokenAsync(string idToken, CancellationToken ct)
    {
        JsonWebKeySet jwks = await GetJwksAsync("jwks_apple", AppleJwksUrl, ct);
        return ValidateAndExtract(idToken, AppleIssuer, _appleAudience, jwks, "Apple");
    }

    private OAuthUserInfo ValidateAndExtract(
        string idToken, string issuer, string audience, JsonWebKeySet jwks, string providerName)
    {
        TokenValidationParameters parameters = new()
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKeys = jwks.Keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
        };

        JwtSecurityTokenHandler handler = new();
        ClaimsPrincipal principal;

        try
        {
            principal = handler.ValidateToken(idToken, parameters, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Provider} ID token validation failed", providerName);
            throw new InvalidOperationException($"Invalid {providerName} ID token.", ex);
        }

        string email = principal.FindFirst(ClaimTypes.Email)?.Value
                     ?? principal.FindFirst("email")?.Value
                     ?? throw new InvalidOperationException($"{providerName} token missing email claim.");

        string sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? principal.FindFirst("sub")?.Value
                     ?? throw new InvalidOperationException($"{providerName} token missing sub claim.");

        // Apple only returns the name on the FIRST sign-in; subsequent sign-ins omit it.
        string? given = principal.FindFirst(ClaimTypes.GivenName)?.Value
                     ?? principal.FindFirst("given_name")?.Value;

        return new OAuthUserInfo(email.ToLowerInvariant(), sub, given);
    }

    /// <summary>
    /// Fetches the JWKS from the provider and caches it in Redis for 1 hour.
    /// Google/Apple rotate keys infrequently; 1h is safe and avoids hammering the endpoint.
    /// Caching is best-effort — a Redis outage must not block sign-in.
    /// </summary>
    private async Task<JsonWebKeySet> GetJwksAsync(string cacheKey, string url, CancellationToken ct)
    {
        byte[]? cached = await TryGetCacheAsync(cacheKey, ct);

        if (cached is not null)
            return new JsonWebKeySet(Encoding.UTF8.GetString(cached));

        using HttpClient client = httpFactory.CreateClient("OAuthJwks");
        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        string jwksJson = await response.Content.ReadAsStringAsync(ct);

        await TrySetCacheAsync(cacheKey, Encoding.UTF8.GetBytes(jwksJson), ct);

        logger.LogInformation("Fetched and cached JWKS from {Url}", url);
        return new JsonWebKeySet(jwksJson);
    }

    private async Task<byte[]?> TryGetCacheAsync(string cacheKey, CancellationToken ct)
    {
        try
        {
            return await cache.GetAsync(cacheKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWKS cache read failed for {CacheKey} — fetching from provider", cacheKey);
            return null;
        }
    }

    private async Task TrySetCacheAsync(string cacheKey, byte[] value, CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(
                cacheKey,
                value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWKS cache write failed for {CacheKey}", cacheKey);
        }
    }
}
