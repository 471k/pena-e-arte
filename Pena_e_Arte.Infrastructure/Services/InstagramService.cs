using System.Globalization;
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
/// Only Instagram Business or Creator accounts can connect; Meta's consent screen refuses personal ones.
/// </summary>
public sealed class InstagramService(
    IHttpClientFactory httpFactory,
    IOptions<InstagramOptions> options,
    ILogger<InstagramService> logger) : IInstagramService
{
    private readonly InstagramOptions _opts = options.Value;

    // The retired Basic Display flow used api.instagram.com/oauth/authorize with the scopes
    // instagram_basic,user_media. Both are gone: the current authorize endpoint is on www.instagram.com and
    // the one scope this app needs (username + media of the connected account) is instagram_business_basic.
    internal const string AuthorizeEndpoint = "https://www.instagram.com/oauth/authorize";
    internal const string Scope = "instagram_business_basic";

    public string BuildAuthorizationUrl(string state) =>
        AuthorizeEndpoint +
        $"?client_id={Uri.EscapeDataString(_opts.AppId)}" +
        $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
        $"&scope={Scope}" +
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
            new("code",          StripCodeSuffix(code)),
        ]);

        HttpResponseMessage shortResponse =
            await client.PostAsync("https://api.instagram.com/oauth/access_token", form, ct);
        shortResponse.EnsureSuccessStatusCode();

        ShortToken shortToken = ParseShortToken(
            await shortResponse.Content.ReadAsStringAsync(ct));

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

        string? accountId = await TryGetAccountIdAsync(client, longToken.AccessToken, ct);

        return new InstagramTokenResponse(
            longToken.AccessToken,
            longToken.TokenType,
            longToken.ExpiresIn,
            shortToken.UserId,
            accountId);
    }

    /// <summary>
    /// The token exchange returns an app-scoped user id; /me?fields=user_id returns the account's
    /// professional ID, which Meta's Deauthorize / Data Deletion callbacks may use instead. Best effort:
    /// a failure here must never break Connect, it only means erasure can match on the app-scoped id alone.
    /// </summary>
    private async Task<string?> TryGetAccountIdAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        try
        {
            HttpResponseMessage response = await client.GetAsync(
                "https://graph.instagram.com/me?fields=user_id" +
                $"&access_token={Uri.EscapeDataString(accessToken)}", ct);
            response.EnsureSuccessStatusCode();

            return ParseAccountId(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read the Instagram professional account id; erasure will match the app-scoped id only");
            return null;
        }
    }

    /// <summary>Reads user_id from a /me response; Meta returns it as a string or a number.</summary>
    internal static string? ParseAccountId(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("user_id", out JsonElement idElement))
            return null;

        string? id = idElement.ValueKind == JsonValueKind.Number ? idElement.GetRawText() : idElement.GetString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>Instagram appends "#_" to the redirect's code; it is not part of the code.</summary>
    internal static string StripCodeSuffix(string code) =>
        code.EndsWith("#_", StringComparison.Ordinal) ? code[..^2] : code;

    /// <summary>
    /// The short-lived token response is wrapped in a "data" array with a string user_id
    /// ({"data":[{"access_token":..,"user_id":"..","permissions":".."}]}); the retired Basic Display API
    /// returned a flat object with a numeric user_id. Accept both so a response-shape change on Meta's side
    /// does not silently break Connect.
    /// </summary>
    internal static ShortToken ParseShortToken(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement item = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data))
        {
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                throw new InvalidOperationException("Empty Instagram token response.");
            item = data[0];
        }

        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("access_token", out JsonElement tokenElement)
            || tokenElement.GetString() is not { Length: > 0 } accessToken)
            throw new InvalidOperationException("Empty Instagram token response.");

        string userId = item.TryGetProperty("user_id", out JsonElement idElement)
            ? idElement.ValueKind == JsonValueKind.Number ? idElement.GetRawText() : idElement.GetString() ?? ""
            : "";

        return new ShortToken(accessToken, userId);
    }

    internal sealed record ShortToken(string AccessToken, string UserId);

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
                    item.Id, item.MediaType, item.MediaUrl, item.ThumbnailUrl, item.Caption, ParseTimestamp(item.Timestamp)));
            }

            nextUrl = page.Paging?.Next;
        }

        logger.LogInformation("Fetched {Count} media items from Instagram", all.Count);
        return all;
    }

    // Instagram sends the offset without a colon ("2026-10-02T13:05:00+0000"), which
    // System.Text.Json's DateTime converter rejects, so the field is read as a string and parsed here.
    internal static DateTime ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).UtcDateTime;

    private string BuildMediaUrl(string accessToken) =>
        "https://graph.instagram.com/me/media" +
        "?fields=id,media_type,media_url,thumbnail_url,caption,timestamp" +
        "&limit=50" +
        $"&access_token={Uri.EscapeDataString(accessToken)}";

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
        [property: JsonPropertyName("timestamp")] string Timestamp);

    private sealed record MediaPageDto(
        [property: JsonPropertyName("data")] List<MediaItemDto> Data,
        [property: JsonPropertyName("paging")] PagingDto? Paging);

    private sealed record PagingDto(
        [property: JsonPropertyName("next")] string? Next,
        [property: JsonPropertyName("previous")] string? Previous);
}
