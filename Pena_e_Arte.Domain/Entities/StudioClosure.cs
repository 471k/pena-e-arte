namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A studio-wide date range where no artist is bookable (public holiday, studio-wide
/// vacation, renovation, etc.), independent of any individual artist's schedule/time-off.
/// </summary>
public class StudioClosure : TenantEntity
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}
