using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Instagram API with Instagram Login (current API — Basic Display API was
/// shut down December 4, 2024). No SDK — raw IHttpClientFactory calls.
/// </summary>
public sealed class InstagramService(
    IHttpClientFactory httpFactory,
    IOptions<InstagramOptions> options,
    ILogger<InstagramService> logger) : IInstagramService
{
    private readonly InstagramOptions _opts = options.Value;

    public string BuildAuthorizationUrl(string state) =>
        "https://api.instagram.com/oauth/authorize" +
        $"?client_id={Uri.EscapeDataString(_opts.AppId)}" +
        $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
        "&scope=instagram_basic,user_media" +
        "&response_type=code" +
        $"&state={Uri.EscapeDataString(state)}";

    public async Task<InstagramTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        FormUrlEncodedContent form = new([
            new("client_id",     _opts.AppId),
            new("client_secret", _opts.AppSecret),
            new("grant_type",    "authorization_code"),
            new("redirect_uri",  _opts.RedirectUri),
            new("code",          code),
        ]);

        HttpResponseMessage shortResponse =
            await client.PostAsync("https://api.instagram.com/oauth/access_token", form, ct);
        shortResponse.EnsureSuccessStatusCode();

        ShortTokenDto shortToken =
            await shortResponse.Content.ReadFromJsonAsync<ShortTokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Instagram token response.");

        string longUrl =
            "https://graph.instagram.com/access_token" +
            "?grant_type=ig_exchange_token" +
            $"&client_secret={Uri.EscapeDataString(_opts.AppSecret)}" +
            $"&access_token={Uri.EscapeDataString(shortToken.AccessToken)}";

        HttpResponseMessage longResponse = await client.GetAsync(longUrl, ct);
        longResponse.EnsureSuccessStatusCode();

        LongTokenDto longToken =
            await longResponse.Content.ReadFromJsonAsync<LongTokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Instagram long token response.");

        return new InstagramTokenResponse(
            longToken.AccessToken,
            longToken.TokenType,
            longToken.ExpiresIn,
            shortToken.UserId.ToString());
    }

    public async Task<(string NewToken, DateTime NewExpiry)> RefreshTokenAsync(
        string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        string url =
            "https://graph.instagram.com/refresh_access_token" +
            "?grant_type=ig_refresh_token" +
            $"&access_token={Uri.EscapeDataString(accessToken)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        LongTokenDto dto =
            await response.Content.ReadFromJsonAsync<LongTokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Instagram refresh response.");

        return (dto.AccessToken, DateTime.UtcNow.AddSeconds(dto.ExpiresIn));
    }

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        string url =
            "https://graph.instagram.com/me?fields=username" +
            $"&access_token={Uri.EscapeDataString(accessToken)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        return doc.RootElement.GetProperty("username").GetString()
               ?? throw new InvalidOperationException("Username missing from Instagram response.");
    }

    public async Task<List<InstagramMediaItem>> GetMediaAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        List<InstagramMediaItem> all = [];
        string? nextUrl = BuildMediaUrl(accessToken);

        while (nextUrl is not null)
        {
            HttpResponseMessage response = await client.GetAsync(nextUrl, ct);
            response.EnsureSuccessStatusCode();

            MediaPageDto? page = await response.Content.ReadFromJsonAsync<MediaPageDto>(ct);
            if (page is null) break;

            foreach (MediaItemDto item in page.Data)
            {
                if (item.MediaType is not ("IMAGE" or "CAROUSEL_ALBUM")) continue;
                if (item.MediaUrl is null && item.ThumbnailUrl is null) continue;

                all.Add(new InstagramMediaItem(
                    item.Id, item.MediaType, item.MediaUrl, item.ThumbnailUrl, item.Caption, item.Timestamp));
            }

            nextUrl = page.Paging?.Next;
        }

        logger.LogInformation("Fetched {Count} media items from Instagram", all.Count);
        return all;
    }

    private string BuildMediaUrl(string accessToken) =>
        "https://graph.instagram.com/me/media" +
        "?fields=id,media_type,media_url,thumbnail_url,caption,timestamp" +
        "&limit=50" +
        $"&access_token={Uri.EscapeDataString(accessToken)}";

    private sealed record ShortTokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("user_id")] long UserId);

    private sealed record LongTokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record MediaItemDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("media_type")] string MediaType,
        [property: JsonPropertyName("media_url")] string? MediaUrl,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl,
        [property: JsonPropertyName("caption")] string? Caption,
        [property: JsonPropertyName("timestamp")] DateTime Timestamp);

    private sealed record MediaPageDto(
        [property: JsonPropertyName("data")] List<MediaItemDto> Data,
        [property: JsonPropertyName("paging")] PagingDto? Paging);

    private sealed record PagingDto(
        [property: JsonPropertyName("next")] string? Next,
        [property: JsonPropertyName("previous")] string? Previous);
}
