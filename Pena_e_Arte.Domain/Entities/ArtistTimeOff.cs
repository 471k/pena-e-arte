namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A specific date range where the artist is unavailable (holiday, sick leave, etc.)
/// </summary>
public class ArtistTimeOff : TenantEntity
{
    public Guid ArtistId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;

    public Artist Artist { get; set; } = null!;
}
