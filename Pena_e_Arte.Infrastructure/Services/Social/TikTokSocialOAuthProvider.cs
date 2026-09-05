using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// TikTok Login Kit for Web (OAuth 2.0 with PKCE-optional authorization-code flow —
/// no business-account requirement, the lowest-friction platform to add after
/// Instagram). Endpoint paths below are stable as of this writing but were NOT
/// re-verified against TikTok's live Developer Portal in this change — confirm
/// scopes/response field names there before relying on this in production, per
/// CLAUDE.md's "don't invent endpoint shapes from memory" rule for third-party APIs.
/// </summary>
public sealed class TikTokSocialOAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<TikTokOptions> options) : ISocialOAuthProvider
{
    private readonly TikTokOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.TikTok;

    public bool IsConfigured => !string.IsNullOrEmpty(_opts.ClientKey);

    public string BuildAuthorizationUrl(string state) =>
        "https://www.tiktok.com/v2/auth/authorize/" +
        $"?client_key={Uri.EscapeDataString(_opts.ClientKey)}" +
        $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
        "&scope=user.info.basic" +
        "&response_type=code" +
        $"&state={Uri.EscapeDataString(state)}";

    public async Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("TikTok");

        FormUrlEncodedContent form = new([
            new("client_key",    _opts.ClientKey),
            new("client_secret", _opts.ClientSecret),
            new("grant_type",    "authorization_code"),
            new("redirect_uri",  _opts.RedirectUri),
            new("code",          code),
        ]);

        HttpResponseMessage response =
            await client.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", form, ct);
        response.EnsureSuccessStatusCode();

        TokenDto token = await response.Content.ReadFromJsonAsync<TokenDto>(ct)
            ?? throw new InvalidOperationException("Empty TikTok token response.");

        return new SocialOAuthTokenResponse(
            token.AccessToken, token.OpenId, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("TikTok");

        using HttpRequestMessage request = new(
            HttpMethod.Get, "https://open.tiktokapis.com/v2/user/info/?fields=display_name");
        request.Headers.Authorization = new("Bearer", accessToken);

        HttpResponseMessage response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        UserInfoEnvelopeDto? envelope = await response.Content.ReadFromJsonAsync<UserInfoEnvelopeDto>(ct);

        return envelope?.Data?.User?.DisplayName
               ?? throw new InvalidOperationException("Username missing from TikTok response.");
    }

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("open_id")] string OpenId,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record UserInfoEnvelopeDto([property: JsonPropertyName("data")] UserInfoDataDto? Data);
    private sealed record UserInfoDataDto([property: JsonPropertyName("user")] UserInfoUserDto? User);
    private sealed record UserInfoUserDto([property: JsonPropertyName("display_name")] string? DisplayName);
}
