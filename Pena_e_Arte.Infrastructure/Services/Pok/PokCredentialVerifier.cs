using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Pok;

/// <summary>
/// C-7: before an owner's POK credentials are saved —
/// 1. logs in once with the keys (POST /auth/sdk/login, the same call PokAuthClient makes before every payment);
/// 2. reads a random, non-existent order under the entered merchant id. POK answers 404 "Order not found" when
///    the keys belong to that merchant and 403 "Access Forbidden!" when they don't (measured on POK staging,
///    2026-10-21). Read-only: nothing is created at POK.
/// Nothing is cached — unlike PokAuthClient's token, a token for unsaved keys must not outlive this check.
/// Never logs the keys, the token or the merchant id.
/// </summary>
public sealed class PokCredentialVerifier(
    IOptions<PokOptions> options, IHttpClientFactory httpClientFactory, ILogger<PokCredentialVerifier> logger)
    : IPokCredentialVerifier
{
    private readonly PokOptions _options = options.Value;

    public async Task<PokCredentialCheckResult> CheckAsync(
        string keyId, string keySecret, string merchantId, CancellationToken ct)
    {
        HttpClient http = httpClientFactory.CreateClient("Pok");
        try
        {
            (PokCredentialCheckResult loginResult, string? token) = await LoginAsync(http, keyId, keySecret, ct);
            if (token is null)
                return loginResult;

            return await CheckMerchantAsync(http, token, merchantId, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("POK credential check: POK unreachable ({ErrorType})", ex.GetType().Name);
            return PokCredentialCheckResult.PokUnreachable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("POK credential check: POK timed out");
            return PokCredentialCheckResult.PokUnreachable;
        }
    }

    private async Task<(PokCredentialCheckResult Result, string? Token)> LoginAsync(
        HttpClient http, string keyId, string keySecret, CancellationToken ct)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"{_options.BaseUrl}/auth/sdk/login")
        {
            Content = JsonContent.Create(new { keyId, keySecret })
        };
        using HttpResponseMessage response = await http.SendAsync(req, ct);

        if (response.IsSuccessStatusCode)
        {
            LoginResponse? body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            if (!string.IsNullOrEmpty(body?.Data?.AccessToken))
                return (PokCredentialCheckResult.Valid, body.Data.AccessToken);

            logger.LogWarning("POK credential check: login answered {StatusCode} without a token", (int)response.StatusCode);
            return (PokCredentialCheckResult.PokUnreachable, null);
        }

        // POK staging answers wrong and unknown keys with 400 "Your credentials are incorrect" (2026-10-21).
        // Any 4xx is POK's verdict on the keys; 408 / 429 and every 5xx are POK not answering the question.
        if (IsVerdict(response.StatusCode))
        {
            logger.LogInformation("POK credential check: keys refused ({StatusCode})", (int)response.StatusCode);
            return (PokCredentialCheckResult.InvalidKeys, null);
        }

        logger.LogWarning("POK credential check: login answered {StatusCode}", (int)response.StatusCode);
        return (PokCredentialCheckResult.PokUnreachable, null);
    }

    private async Task<PokCredentialCheckResult> CheckMerchantAsync(
        HttpClient http, string token, string merchantId, CancellationToken ct)
    {
        string probeOrderId = Guid.NewGuid().ToString();
        using HttpRequestMessage req = new(HttpMethod.Get,
            $"{_options.BaseUrl}/merchants/{Uri.EscapeDataString(merchantId)}/sdk-orders/{probeOrderId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(req, ct);

        switch (response.StatusCode)
        {
            // "Order not found" under this merchant = the keys are this merchant's (the order id is random).
            case HttpStatusCode.NotFound:
            case var code when (int)code is >= 200 and < 300:
                return PokCredentialCheckResult.Valid;

            // "Access Forbidden!" = these keys may not act for this merchant id; 400 = not a merchant id POK accepts.
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.BadRequest:
                logger.LogInformation("POK credential check: merchant id refused for these keys ({StatusCode})", (int)response.StatusCode);
                return PokCredentialCheckResult.MerchantMismatch;

            default:
                logger.LogWarning("POK credential check: merchant check answered {StatusCode}", (int)response.StatusCode);
                return PokCredentialCheckResult.PokUnreachable;
        }
    }

    private static bool IsVerdict(HttpStatusCode status) =>
        (int)status is >= 400 and < 500
        && status is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests;

    private sealed class LoginResponse
    {
        [JsonPropertyName("data")] public LoginResponseData? Data { get; init; }
    }

    private sealed class LoginResponseData
    {
        [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
    }
}
