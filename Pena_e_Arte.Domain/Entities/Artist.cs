using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Artist : TenantEntity
{
    public Guid? UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Canonical <see cref="Pena_e_Arte.Domain.Constants.TattooStyle"/> values this artist specializes in.</summary>
    public List<string> Specializations { get; set; } = [];

    /// <summary>Hourly rate in the studio's currency (Studio.Currency) — the base for percent
    /// deposit rules. Null = not set.</summary>
    public decimal? HourlyRate { get; set; }

    /// <summary>This artist's own commission rate, overriding the studio's
    /// <see cref="Studio.DefaultCommissionRate"/> (A07a). A percentage of what the client paid that
    /// goes to the artist: 60.00 means 60% (0–100). It was documented as a percent before A07a and
    /// the column is decimal(5,2), so it stays a percent rather than becoming a fraction. Null =
    /// use the studio's rate. Set only by the owner, through PUT /artists/{id}/pay-model; never
    /// part of ArtistResponse, which clients can read.</summary>
    public decimal? CommissionRate { get; set; }

    /// <summary>This artist's own arrangement, overriding <see cref="Studio.DefaultPayArrangement"/>.
    /// Null = use the studio's.</summary>
    public ArtistPayArrangement? PayArrangement { get; set; }

    /// <summary>The studio's cut per session for this artist under FlatFeePerSession, overriding
    /// <see cref="Studio.DefaultFlatFeePerSession"/>. Null = use the studio's.</summary>
    public decimal? FlatFeePerSession { get; set; }
    public string? Slug { get; private set; }
    public string? Bio { get; set; }
    public string? ProfileImageUrl { get; set; }

    /// <summary>False for seed/test records that should not appear in client-facing dropdowns.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>URL of the artist's profile photo. Null when not set — show initials fallback in the UI.</summary>
    public string? AvatarUrl { get; set; }

    public void SetSlug(string slug) => Slug = slug;

    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<TattooRecord> TattooRecords { get; set; } = [];
    public ICollection<PortfolioImage> Portfolio { get; set; } = [];
    public ICollection<ArtistSchedule> Schedule { get; set; } = [];
    public ICollection<ArtistTimeOff> TimeOff { get; set; } = [];
    public ICollection<Client> Clients { get; set; } = [];
}
