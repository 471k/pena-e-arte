using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// Meta Graph API Business Discovery — officially sanctioned for reading another
/// public Business/Creator account's public data (bio included), not scraping.
/// Requires the app's own long-lived Business/Creator access token (App:AccessToken)
/// and only works when the *target* handle is itself a Business/Creator account, same
/// constraint the existing artist Instagram OAuth flow already has. A personal
/// Instagram account cannot be manually verified this way — it needs OAuth Connect
/// instead (which has the same Business/Creator requirement; a genuinely personal
/// account can't be verified on either path, which is a real platform limitation, not
/// a gap in this feature).
/// </summary>
public sealed class InstagramBioChecker(
    IHttpClientFactory httpFactory,
    IOptions<InstagramOptions> options) : ISocialBioChecker
{
    private readonly InstagramOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.Instagram;

    public bool IsSupported => !string.IsNullOrEmpty(_opts.AppId);

    public async Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        // Meta "app access token" format ({app-id}|{app-secret}) — the standard way to
        // call Graph API app-level endpoints without a user login, per Meta's docs.
        string appAccessToken = $"{_opts.AppId}|{_opts.AppSecret}";
        string url =
            $"https://graph.facebook.com/v21.0/{_opts.AppId}" +
            $"?fields=business_discovery.username({Uri.EscapeDataString(handle)}){{biography}}" +
            $"&access_token={Uri.EscapeDataString(appAccessToken)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return false;

        BusinessDiscoveryEnvelopeDto? envelope =
            await response.Content.ReadFromJsonAsync<BusinessDiscoveryEnvelopeDto>(ct);

        string? bio = envelope?.BusinessDiscovery?.Biography;
        return bio is not null && bio.Contains(code, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BusinessDiscoveryEnvelopeDto(
        [property: JsonPropertyName("business_discovery")] BusinessDiscoveryDto? BusinessDiscovery);

    private sealed record BusinessDiscoveryDto(
        [property: JsonPropertyName("biography")] string? Biography);
}
