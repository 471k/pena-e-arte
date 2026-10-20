using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Pok;

/// <summary>
/// C-7: logs in to POK once with the keys an owner is about to save (POST /auth/sdk/login, the same
/// call PokAuthClient makes before every payment). Nothing is cached — unlike PokAuthClient's token,
/// a token for unsaved keys must not outlive this check. Never logs the keys or the token.
/// </summary>
public sealed class PokCredentialVerifier(
    IOptions<PokOptions> options, IHttpClientFactory httpClientFactory, ILogger<PokCredentialVerifier> logger)
    : IPokCredentialVerifier
{
    private readonly PokOptions _options = options.Value;

    public async Task<PokCredentialCheckResult> CheckKeysAsync(string keyId, string keySecret, CancellationToken ct)
    {
        HttpClient http = httpClientFactory.CreateClient("Pok");
        using HttpRequestMessage req = new(HttpMethod.Post, $"{_options.BaseUrl}/auth/sdk/login")
        {
            Content = JsonContent.Create(new { keyId, keySecret })
        };

        try
        {
            using HttpResponseMessage response = await http.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
            {
                LoginResponse? body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
                if (!string.IsNullOrEmpty(body?.Data?.AccessToken))
                    return PokCredentialCheckResult.Valid;

                logger.LogWarning("POK key check: login answered {StatusCode} without a token", (int)response.StatusCode);
                return PokCredentialCheckResult.PokUnreachable;
            }

            // A 4xx is POK's verdict on the keys; 408 / 429 and every 5xx are POK not answering the question.
            if ((int)response.StatusCode is >= 400 and < 500
                && response.StatusCode is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests)
            {
                logger.LogInformation("POK key check: keys refused ({StatusCode})", (int)response.StatusCode);
                return PokCredentialCheckResult.InvalidKeys;
            }

            logger.LogWarning("POK key check: POK answered {StatusCode}", (int)response.StatusCode);
            return PokCredentialCheckResult.PokUnreachable;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("POK key check: POK unreachable ({ErrorType})", ex.GetType().Name);
            return PokCredentialCheckResult.PokUnreachable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("POK key check: POK timed out");
            return PokCredentialCheckResult.PokUnreachable;
        }
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("data")] public LoginResponseData? Data { get; init; }
    }

    private sealed class LoginResponseData
    {
        [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
    }
}
