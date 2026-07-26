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
    public string Username { get; set; } = "";

    /// <summary>AES-256-GCM encrypted long-lived access token.</summary>
    public string EncryptedToken { get; set; } = "";

    public DateTime TokenExpiresAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
