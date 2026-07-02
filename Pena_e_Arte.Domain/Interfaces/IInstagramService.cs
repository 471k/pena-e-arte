namespace Pena_e_Arte.Domain.Interfaces;

public record InstagramTokenResponse(
    string AccessToken,
    string TokenType,
    long   ExpiresIn,
    string UserId);

public record InstagramMediaItem(
    string   Id,
    string   MediaType,
    string?  MediaUrl,
    string?  ThumbnailUrl,
    string?  Caption,
    DateTime Timestamp);

public interface IInstagramService
{
    /// <summary>Builds the Instagram OAuth authorization URL for the given signed state.</summary>
    string BuildAuthorizationUrl(string state);

    /// <summary>Exchange the OAuth code for a short-lived token, then upgrade to long-lived.</summary>
    Task<InstagramTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);

    /// <summary>Refresh a long-lived token. Returns updated token + new expiry.</summary>
    Task<(string NewToken, DateTime NewExpiry)> RefreshTokenAsync(string accessToken, CancellationToken ct);

    /// <summary>Fetch the user's username from the Instagram API.</summary>
    Task<string> GetUsernameAsync(string accessToken, CancellationToken ct);

    /// <summary>Fetch all IMAGE and CAROUSEL_ALBUM media items (handles pagination).</summary>
    Task<List<InstagramMediaItem>> GetMediaAsync(string accessToken, CancellationToken ct);
}
