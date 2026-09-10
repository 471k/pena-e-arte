namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One entry per day-of-week the studio is open. Mirrors ArtistSchedule's shape exactly
/// (IsAvailable renamed to IsOpen — same semantics, studio- not artist-scoped). No entry
/// for a given DayOfWeek means the studio is closed that day, same "absence = closed"
/// convention ArtistSchedule already uses.
/// </summary>
public class StudioHours : TenantEntity
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsOpen { get; set; } = true;
}
