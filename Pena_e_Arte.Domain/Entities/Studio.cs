namespace Pena_e_Arte.Domain.Entities;

public class Studio
{
    public Guid     Id              { get; init; } = Guid.NewGuid();
    public string   Name            { get; set; }  = string.Empty;
    public string   Slug            { get; set; }  = string.Empty;
    public string   City            { get; set; }  = string.Empty;
    public string   OwnerEmail      { get; set; }  = string.Empty;
    public string?  Description     { get; set; }
    public string?  CoverImageUrl   { get; set; }
    public string?  PhoneNumber     { get; set; }
    public string?  InstagramHandle { get; set; }
    public double   Latitude        { get; set; }
    public double   Longitude       { get; set; }
    /// <summary>
    /// Controls whether this studio is visible in public-facing endpoints
    /// (public portfolio, studio map) and whether tenant access is permitted.
    /// <para>
    /// NOTE: The SP-02 spec referenced an <c>IsPublished</c> field. No such field
    /// exists. <c>IsActive</c> serves the same purpose — a studio that should not
    /// appear publicly is simply deactivated by the issuer. Do not add
    /// <c>IsPublished</c> without first updating <c>docs/claude/architecture.md</c>.
    /// </para>
    /// </summary>
    public bool     IsActive              { get; set; } = true;
    public bool     ShowPlatformBranding  { get; private set; } = true;
    public DateTime? SlugLockedAt   { get; set; }

    public void UpdateBranding(bool show) => ShowPlatformBranding = show;
    public DateTime TrialExpiresAt        { get; set; }
    public string?  StripeCustomerId { get; set; }
    public DateTime CreatedAt       { get; init; } = DateTime.UtcNow;

    public Guid? PendingReferralCodeId { get; set; }

    public Subscription? Subscription { get; set; }
}
