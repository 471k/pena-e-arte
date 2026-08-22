using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// Same Meta Graph API Business Discovery field as InstagramBioChecker — officially
/// sanctioned, not scraping. Only works for Facebook Pages that are Business/Creator
/// accounts, same platform limitation noted there.
/// </summary>
public sealed class FacebookBioChecker(
    IHttpClientFactory httpFactory,
    IOptions<FacebookOptions> facebookOptions) : ISocialBioChecker
{
    private readonly FacebookOptions _fbOpts = facebookOptions.Value;

    public SocialPlatform Platform => SocialPlatform.Facebook;

    public bool IsSupported => !string.IsNullOrEmpty(_fbOpts.AppId);

    public async Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Facebook");

        // Meta "app access token" format ({app-id}|{app-secret}) — see InstagramBioChecker.
        string appAccessToken = $"{_fbOpts.AppId}|{_fbOpts.AppSecret}";
        string url =
            $"https://graph.facebook.com/v21.0/{_fbOpts.AppId}" +
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
