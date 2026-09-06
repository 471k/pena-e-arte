namespace Pena_e_Arte.Domain.Entities;

public class Studio
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? InstagramHandle { get; set; }
    public string? Nipt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>
    /// Gates tenant access entirely — a deactivated studio's owner/artists cannot use the
    /// app. Distinct from <c>IsPublished</c> (below), which gates only studio-directory
    /// listing. See architecture.md's "IsActive vs IsPublished" section.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True for a studio auto-provisioned by RegisterSoloArtistCommand for an
    /// independent artist with no pre-existing studio. Never set any other way.</summary>
    public bool IsSolo { get; set; }

    /// <summary>Controls listing in studio-directory surfaces only (Studio Map, /discover
    /// Studios tab, StudioPortfolioPage) — distinct from IsActive, which gates tenant access.
    /// True by default for every normally-registered studio. False on creation only for an
    /// IsSolo studio, until it has real City/Latitude/Longitude (see UpdateMyStudioHandler).
    /// See architecture.md's "IsActive vs IsPublished" section — this is the field that
    /// section names as the correct fix for exactly this situation.</summary>
    public bool IsPublished { get; set; } = true;

    public bool ShowPlatformBranding { get; private set; } = true;
    public DateTime? SlugLockedAt { get; set; }

    /// <summary>Set when this studio is soft-closed after its owner accepted a
    /// StudioJoinInvite to join a different studio (Phase 6). IsActive is also false at
    /// that point — this field distinguishes "closed by its own owner joining elsewhere"
    /// from other IsActive=false paths (e.g. admin suspension). Historical data
    /// (appointments, clients, portfolio, payments) is retained, never deleted or copied.</summary>
    public DateTime? ClosedAt { get; set; }

    public void UpdateBranding(bool show) => ShowPlatformBranding = show;
    public DateTime TrialExpiresAt { get; set; }
    public string? StripeCustomerId { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Running total of Cloudflare R2 usage (photos, consent PDFs, design revisions) in
    /// bytes. Incremented/decremented at each successful R2 write/delete rather than
    /// queried live from R2 — see PlanLimitBehavior / IPlanLimitService.
    /// </summary>
    public long StorageUsageBytes { get; set; }

    public Guid? PendingReferralCodeId { get; set; }

    public Subscription? Subscription { get; set; }
}
