using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Waitlist : TenantEntity
{
    /// <summary>Null means "any artist" — matched against every artist's freed slots.</summary>
    public Guid? ArtistId { get; set; }

    /// <summary>Set when the entry belongs to a signed-in client; null for a guest entry.</summary>
    public Guid? ClientId { get; set; }

    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }

    public DateTime PreferredDateFrom { get; set; }
    public DateTime PreferredDateTo { get; set; }

    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;

    /// <summary>Set when Status transitions to Notified — the 24h claim window starts here.</summary>
    public DateTime? NotifiedAt { get; set; }

    public string? Notes { get; set; }

    public Artist? Artist { get; set; }
    public Client? Client { get; set; }
}
