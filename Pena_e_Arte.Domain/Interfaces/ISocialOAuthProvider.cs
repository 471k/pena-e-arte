using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

public record SocialOAuthTokenResponse(string AccessToken, string? ExternalUserId, DateTime? ExpiresAt);

/// <summary>
/// One platform's OAuth "Connect" implementation for social verification. Every one of
/// the five platforms gets a registered implementation of this interface (so the
/// factory below never throws PlatformNotSupportedException in normal operation) —
/// but a given deployment may not have real credentials for all five yet, which
/// IsConfigured reports.
/// </summary>
public interface ISocialOAuthProvider
{
    SocialPlatform Platform { get; }

    /// <summary>
    /// False when this platform's OAuth client credentials aren't configured yet (e.g.
    /// Facebook/X pending external app review, or a platform simply not deployed with
    /// real secrets on this environment). Callers must check this before building a
    /// connect URL and return a clear "not yet available" response, not a generic 500.
    /// </summary>
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(string state);

    Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);

    Task<string> GetUsernameAsync(string accessToken, CancellationToken ct);
}
