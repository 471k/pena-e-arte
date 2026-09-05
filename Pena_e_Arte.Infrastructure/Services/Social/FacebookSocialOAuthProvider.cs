using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// Facebook Login OAuth (Meta Graph API family — same family as Instagram, but its own
/// app permission set and app-review requirement). The dialog/token endpoints below are
/// stable, well-documented Graph API URLs, but the exact permission scope needed to
/// read a Page's public identity — and whether that scope requires Meta App Review
/// before it works for non-test users — was NOT re-verified against Meta's current
/// Graph API version/App Review requirements in this change. Compiles and is wired for
/// the moment real app credentials + review approval exist; do not assume review is
/// unnecessary. See Part 4c of the originating overnight prompt for the full caveat.
/// </summary>
public sealed class FacebookSocialOAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<FacebookOptions> options) : ISocialOAuthProvider
{
    private const string GraphVersion = "v21.0";
    private readonly FacebookOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.Facebook;

    public bool IsConfigured => !string.IsNullOrEmpty(_opts.AppId);

    public string BuildAuthorizationUrl(string state) =>
        $"https://www.facebook.com/{GraphVersion}/dialog/oauth" +
        $"?client_id={Uri.EscapeDataString(_opts.AppId)}" +
        $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
        "&scope=public_profile" +
        $"&state={Uri.EscapeDataString(state)}";

    public async Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Facebook");

        string url =
            $"https://graph.facebook.com/{GraphVersion}/oauth/access_token" +
            $"?client_id={Uri.EscapeDataString(_opts.AppId)}" +
            $"&client_secret={Uri.EscapeDataString(_opts.AppSecret)}" +
            $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
            $"&code={Uri.EscapeDataString(code)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        TokenDto token = await response.Content.ReadFromJsonAsync<TokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Facebook token response.");

        return new SocialOAuthTokenResponse(
            token.AccessToken, null, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Facebook");

        string url =
            $"https://graph.facebook.com/{GraphVersion}/me?fields=name" +
            $"&access_token={Uri.EscapeDataString(accessToken)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        MeDto? me = await response.Content.ReadFromJsonAsync<MeDto>(ct);

        return me?.Name ?? throw new InvalidOperationException("Name missing from Facebook response.");
    }

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record MeDto([property: JsonPropertyName("name")] string? Name);
}
