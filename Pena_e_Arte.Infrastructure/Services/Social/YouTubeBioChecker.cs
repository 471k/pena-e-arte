using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// YouTube Data API v3, channel lookup by handle. Needs only an API key, no OAuth from
/// the target — lowest-friction of all five manual checks. Uses `forHandle`, the
/// current handle-based lookup param; confirm against the live API version if the
/// older `forUsername` param is ever needed as a fallback (YouTube moved to
/// handle-based lookups relatively recently, per the originating spec).
/// </summary>
public sealed class YouTubeBioChecker(
    IHttpClientFactory httpFactory,
    IOptions<YouTubeOptions> options) : ISocialBioChecker
{
    private readonly YouTubeOptions _opts = options.Value;

    public SocialPlatform Platform => SocialPlatform.YouTube;

    public bool IsSupported => !string.IsNullOrEmpty(_opts.ApiKey);

    public async Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("YouTube");

        string url =
            "https://www.googleapis.com/youtube/v3/channels" +
            $"?forHandle={Uri.EscapeDataString(handle)}&part=snippet&key={Uri.EscapeDataString(_opts.ApiKey)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return false;

        ChannelListDto? list = await response.Content.ReadFromJsonAsync<ChannelListDto>(ct);

        string? description = list?.Items?.FirstOrDefault()?.Snippet?.Description;
        return description is not null && description.Contains(code, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ChannelListDto([property: JsonPropertyName("items")] List<ChannelDto>? Items);
    private sealed record ChannelDto([property: JsonPropertyName("snippet")] SnippetDto? Snippet);
    private sealed record SnippetDto([property: JsonPropertyName("description")] string? Description);
}
