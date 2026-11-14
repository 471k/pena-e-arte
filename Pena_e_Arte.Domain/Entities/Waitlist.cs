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

    /// <summary>
    /// The start of the freed slot this entry was offered. Set together with <see cref="NotifiedAt"/> when
    /// the entry flips to Notified (WaitlistMatchExtensions.ClaimNextMatchAsync); null while Waiting and for
    /// rows notified before 2026-11-14, when the slot wasn't recorded.
    /// </summary>
    public DateTime? NotifiedSlotStart { get; set; }

    /// <summary>
    /// The artist of the freed slot. Set with <see cref="NotifiedSlotStart"/>; null while Waiting. Distinct from
    /// <see cref="ArtistId"/>, which is null for an "any artist" entry; this is the artist whose slot opened
    /// (itself null only when the freed appointment had no artist).
    /// </summary>
    public Guid? NotifiedArtistId { get; set; }

    /// <summary>
    /// The freed slot's length. Set with <see cref="NotifiedSlotStart"/>; null while Waiting. Needed to check
    /// the slot is still free before a cascade offers it to the next person, and before the claim page does.
    /// </summary>
    public int? NotifiedSlotDurationMinutes { get; set; }

    public string? Notes { get; set; }

    public Artist? Artist { get; set; }
    public Client? Client { get; set; }
}
