namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A bookkeeping ledger row — NOT a <see cref="Payment"/>. Booth rent tracks what an artist owes
/// the studio (the reverse direction of every Payment row, which models client-to-studio money)
/// and, tonight, involves no card charge at all. See architecture.md Decisions Log for why this
/// is a dedicated entity instead of extending Payment.
/// </summary>
public class BoothRentCharge : TenantEntity
{
    public Guid BoothRentScheduleId { get; set; }
    public Guid ArtistId { get; set; }
    public decimal Amount { get; set; }
    public DateTime ChargedDate { get; set; }
    public bool IsSettled { get; set; }
    public DateTime? SettledAt { get; set; }
    public string? SettledNote { get; set; }

    public BoothRentSchedule Schedule { get; set; } = null!;
    public Artist Artist { get; set; } = null!;
}
