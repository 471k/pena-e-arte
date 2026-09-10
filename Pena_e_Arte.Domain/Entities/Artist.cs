namespace Pena_e_Arte.Domain.Entities;

public class Artist : TenantEntity
{
    public Guid? UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Specializations { get; set; }

    /// <summary>Hourly rate in EUR — the base for percent deposit rules. Null = not set.</summary>
    public decimal? HourlyRate { get; set; }

    /// <summary>Booth-rent commission percent, informational only — not used in any charge
    /// calculation tonight (booth rent is a fixed AmountFixed per BoothRentSchedule). Null = not set.</summary>
    public decimal? CommissionRate { get; set; }
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
