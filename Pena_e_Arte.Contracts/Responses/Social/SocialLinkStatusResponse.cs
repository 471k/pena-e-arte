namespace Pena_e_Arte.Contracts.Responses.Social;

/// <summary>Owner/artist-settings view of one platform's link — verified or not,
/// including whether Connect/manual-code are even available on this deployment.</summary>
public record SocialLinkStatusResponse(
    string Platform,
    string? Handle,
    bool IsVerified,
    DateTime? VerifiedAt,
    string? VerificationMethod,
    bool IsOAuthConfigured,
    bool IsManualCheckSupported,
    bool HasPendingCode,
    DateTime? PendingCodeExpiresAt);
