namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Claims extracted from a validated Google or Apple ID token.
/// The validation has already verified the JWT signature against the provider's JWKS.
/// </summary>
public record OAuthUserInfo(
    string Email,
    string ProviderUserId,
    string? FirstName);

public interface IOAuthTokenValidator
{
    /// <summary>
    /// Validates a Google ID token. Fetches and caches Google's JWKS.
    /// Throws <see cref="InvalidOperationException"/> if the token is invalid or expired.
    /// </summary>
    Task<OAuthUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct);

    /// <summary>
    /// Validates an Apple ID token. Fetches and caches Apple's JWKS.
    /// Throws <see cref="InvalidOperationException"/> if the token is invalid or expired.
    /// </summary>
    Task<OAuthUserInfo> ValidateAppleTokenAsync(string idToken, CancellationToken ct);
}
