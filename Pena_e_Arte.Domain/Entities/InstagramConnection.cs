namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// OAuth connection between an Artist and their Instagram account.
/// No global query filter (see AppDbContext) — the nightly sync job iterates all
/// tenants. Application-layer handlers must filter by ArtistId and verify the
/// artist belongs to the caller's tenant via the (tenant-filtered) Artists set.
/// </summary>
public class InstagramConnection : TenantEntity
{
    public Guid ArtistId { get; set; }
    public string InstagramUserId { get; set; } = "";

    /// <summary>
    /// The account's professional (Instagram) ID from /me?fields=user_id — a different value from the
    /// app-scoped InstagramUserId the token exchange returns. Meta's Deauthorize / Data Deletion callbacks
    /// may identify the user by either, so erasure matches both. Null on rows connected before it existed
    /// or when the lookup failed.
    /// </summary>
    public string? InstagramAccountId { get; set; }
    public string Username { get; set; } = "";

    /// <summary>AES-256-GCM encrypted long-lived access token.</summary>
    public string EncryptedToken { get; set; } = "";

    public DateTime TokenExpiresAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
