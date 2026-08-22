using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// Google OAuth 2.0 authorization-code flow with the youtube.readonly scope. This is
/// deliberately NOT IOAuthTokenValidator — that interface only validates an
/// already-issued Google ID token for "Sign in with Google" login and has no
/// authorization-code exchange or scoped-access-token capability, so it cannot read
/// YouTube channel data. This provider needs its own Google Cloud OAuth client;
/// YouTube's read scope can trigger Google's OAuth consent-screen verification
/// requirements for apps with many users — confirm with whoever manages that Cloud
/// project before relying on this at scale.
/// </summary>
public sealed class YouTubeSocialOAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<YouTubeOptions> options) : ISocialOAuthProvider
{
    private readonly YouTubeOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.YouTube;

    public bool IsConfigured => !string.IsNullOrEmpty(_opts.ClientId);

    public string BuildAuthorizationUrl(string state) =>
        "https://accounts.google.com/o/oauth2/v2/auth" +
        $"?client_id={Uri.EscapeDataString(_opts.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
        "&response_type=code" +
        "&access_type=online" +
        "&scope=https://www.googleapis.com/auth/youtube.readonly" +
        $"&state={Uri.EscapeDataString(state)}";

    public async Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("YouTube");

        FormUrlEncodedContent form = new([
            new("client_id",     _opts.ClientId),
            new("client_secret", _opts.ClientSecret),
            new("grant_type",    "authorization_code"),
            new("redirect_uri",  _opts.RedirectUri),
            new("code",          code),
        ]);

        HttpResponseMessage response =
            await client.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        response.EnsureSuccessStatusCode();

        TokenDto token = await response.Content.ReadFromJsonAsync<TokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Google token response.");

        return new SocialOAuthTokenResponse(
            token.AccessToken, null, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("YouTube");

        using HttpRequestMessage request = new(
            HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?mine=true&part=snippet");
        request.Headers.Authorization = new("Bearer", accessToken);

        HttpResponseMessage response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        ChannelListDto? list = await response.Content.ReadFromJsonAsync<ChannelListDto>(ct);

        return list?.Items?.FirstOrDefault()?.Snippet?.Title
               ?? throw new InvalidOperationException("Channel title missing from YouTube response.");
    }

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record ChannelListDto([property: JsonPropertyName("items")] List<ChannelDto>? Items);
    private sealed record ChannelDto([property: JsonPropertyName("snippet")] SnippetDto? Snippet);
    private sealed record SnippetDto([property: JsonPropertyName("title")] string? Title);
}
