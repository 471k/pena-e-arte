using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One artist's or studio's link to one social platform, with optional verification.
/// No global query filter (see AppDbContext) — same documented shape as
/// InstagramConnection. Every handler must filter by (SubjectType, SubjectId) explicitly
/// and resolve/verify the real owning StudioId from the target entity, never trust
/// ICurrentTenant blindly (an admin-role caller may have no tenant set at all).
///
/// For a Studio-subject row, StudioId == SubjectId (the studio's own id, since Studio is
/// admin-level/unfiltered — a self-referential tenant key, not a bug). For an
/// Artist-subject row, StudioId is that artist's real tenant.
///
/// This entity does not replace InstagramConnection, which keeps owning the artist
/// photo-sync lifecycle exactly as before — see ExchangeInstagramCodeCommand, which
/// upserts a row here alongside its own InstagramConnection write.
/// </summary>
public class SocialAccountLink : TenantEntity
{
    public SocialLinkSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }               // ArtistId or StudioId
    public SocialPlatform Platform { get; set; }
    public string Handle { get; set; } = "";           // display handle, no leading '@'
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public SocialVerificationMethod? VerificationMethod { get; set; }

    // OAuth path only — null otherwise. Discarded immediately for Studio-subject rows
    // (no ongoing sync need — the OAuth handshake there is a one-time identity check);
    // kept + refreshed for Artist-subject rows via the periodic re-verification job.
    public string? ExternalUserId { get; set; }
    public string? EncryptedToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }

    // Manual bio-code path only — null once verified or expired.
    public string? PendingVerificationCode { get; set; }
    public DateTime? PendingCodeExpiresAt { get; set; }
}
