using Pena_e_Arte.Domain.Enums;

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

    /// <summary>Street address — nullable for backfill (pre-existing studios), required at
    /// the app layer (RegisterStudioValidator) for new registrations. Drives live forward-
    /// geocoding on the frontend; never rewritten from a manually-dropped map pin.</summary>
    public string? AddressLine1 { get; set; }

    /// <summary>Suite/unit/floor — always optional, forever.</summary>
    public string? AddressLine2 { get; set; }

    /// <summary>Always optional, forever.</summary>
    public string? PostalCode { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>IANA/Olson timezone identifier (e.g. "Europe/Tirane"). Used only to format
    /// UTC timestamps for display/notifications — never for storage or business-logic
    /// comparisons, which stay UTC throughout. Defaults to the platform's primary market at
    /// registration; owner-correctable in studio settings.</summary>
    public string Timezone { get; set; } = "Europe/Tirane";

    /// <summary>ISO 3166-1 alpha-2, upper-case (e.g. "AL"). Chosen at registration (prefilled from
    /// the geocoded address); owner-editable anytime. Drives only the DEFAULT currency — changing
    /// it never changes Currency.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>ISO 4217, upper-case (e.g. "ALL", "EUR"). The one currency every price and payment
    /// in this studio uses. Locked once money has moved (see StudioCurrencyLock). Never a silent
    /// toggle — architecture.md Decisions Log, "Studio currency (2026-09-27)".</summary>
    public string Currency { get; set; } = string.Empty;

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

    /// <summary>When the owner last confirmed the studio's opening hours (E05a, O02): by saving
    /// the hours form, or by saying the hours registration set for them are right. Null means
    /// nobody has looked at them yet, so the setup checklist shows registration's default
    /// Mon–Fri schedule as "set for you, not yet reviewed" rather than done. Hours rows' own
    /// CreatedAt/UpdatedAt can't carry this: registration's rows get two separate
    /// DateTime.UtcNow reads, so a never-touched row can already have UpdatedAt &gt; CreatedAt.</summary>
    public DateTime? HoursReviewedAt { get; set; }

    /// <summary>How the studio's artists are paid unless an artist has their own arrangement
    /// (DEC10, A07a). Commission by default, as the most common structure, but with no rate: see
    /// <see cref="DefaultCommissionRate"/>.</summary>
    public ArtistPayArrangement DefaultPayArrangement { get; set; } = ArtistPayArrangement.Commission;

    /// <summary>The artist's share under Commission, as a percentage of what the client paid:
    /// 60.00 means 60% to the artist (0–100, the same unit as <see cref="Artist.CommissionRate"/>).
    /// Null by default and never seeded: the sources that quote 50/50 or 60/40 call those figures
    /// illustrative, so a seeded number would read as configured when nobody chose it. While it is
    /// null, a commission artist's share is unknown, never a guess.</summary>
    public decimal? DefaultCommissionRate { get; set; }

    /// <summary>The studio's cut per session under FlatFeePerSession, in the studio's currency.
    /// Null means not set.</summary>
    public decimal? DefaultFlatFeePerSession { get; set; }

    /// <summary>When the owner last confirmed how artists get paid (A07a): by saving the pay-model
    /// form, or by saying the default is right. Null means nobody has looked at it, so the setup
    /// checklist shows it as "set for you, not yet reviewed". Same semantics as
    /// <see cref="HoursReviewedAt"/>.</summary>
    public DateTime? PayModelReviewedAt { get; set; }

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

    /// <summary>
    /// This studio's own POK merchant id — the routing value in every
    /// <c>/merchants/{merchantId}/...</c> call POK makes on its behalf (ADR-0001 Flow A).
    /// Not a secret: it identifies the merchant, it doesn't authenticate as one (that's
    /// <see cref="StudioCredentialRef"/>'s job, resolved via Vault). Null until the owner
    /// connects their POK account in studio settings.
    /// </summary>
    public string? PokMerchantId { get; set; }

    public Subscription? Subscription { get; set; }
}
