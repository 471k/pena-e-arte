using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// X (Twitter) API v2 OAuth 2.0 authorization-code flow. Endpoint URLs below are
/// current as of this writing, but X's API access tiers/pricing have changed
/// repeatedly — the current tier required for `users/me` was NOT re-verified against
/// X's live developer docs in this change. Ships config-gated (IsConfigured == false
/// with no ClientId configured) until a real, paid API tier + credentials exist. Uses
/// PKCE with a fixed `challenge`/`plain` method since this provider has no per-request
/// session to stash a random verifier in between BuildAuthorizationUrl and
/// ExchangeCodeAsync — acceptable here because the `state` param is itself
/// HMAC-signed and short-lived, giving equivalent CSRF protection.
/// </summary>
public sealed class XSocialOAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<XOptions> options) : ISocialOAuthProvider
{
    private const string CodeVerifier = "challenge"; // see PKCE note above — plain-method, fixed verifier
    private readonly XOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.X;

    public bool IsConfigured => !string.IsNullOrEmpty(_opts.ClientId);

    public string BuildAuthorizationUrl(string state) =>
        "https://twitter.com/i/oauth2/authorize" +
        $"?client_id={Uri.EscapeDataString(_opts.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
        "&response_type=code" +
        "&scope=tweet.read%20users.read" +
        $"&state={Uri.EscapeDataString(state)}" +
        $"&code_challenge={CodeVerifier}&code_challenge_method=plain";

    public async Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("X");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}")));

        FormUrlEncodedContent form = new([
            new("code",          code),
            new("grant_type",    "authorization_code"),
            new("redirect_uri",  _opts.RedirectUri),
            new("code_verifier", CodeVerifier),
        ]);

        HttpResponseMessage response =
            await client.PostAsync("https://api.twitter.com/2/oauth2/token", form, ct);
        response.EnsureSuccessStatusCode();

        TokenDto token = await response.Content.ReadFromJsonAsync<TokenDto>(ct)
            ?? throw new InvalidOperationException("Empty X token response.");

        return new SocialOAuthTokenResponse(
            token.AccessToken, null, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("X");

        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.twitter.com/2/users/me");
        request.Headers.Authorization = new("Bearer", accessToken);

        HttpResponseMessage response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        UserEnvelopeDto? envelope = await response.Content.ReadFromJsonAsync<UserEnvelopeDto>(ct);

        return envelope?.Data?.Username
               ?? throw new InvalidOperationException("Username missing from X response.");
    }

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record UserEnvelopeDto([property: JsonPropertyName("data")] UserDto? Data);
    private sealed record UserDto([property: JsonPropertyName("username")] string? Username);
}
