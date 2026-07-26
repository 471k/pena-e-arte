namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One entry per working day-of-week. Artists without entries on a day are unavailable.
/// </summary>
public class ArtistSchedule : TenantEntity
{
    public Guid ArtistId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsAvailable { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
