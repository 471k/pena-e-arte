namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Nightly rollup of TrafficEvent, one row per (Date, StudioId-or-null,
/// Role-or-null-for-guest, CountryCode-or-null). Written by TrafficRollupJob
/// (Hangfire, daily). Non-tenant, same shape reasoning as TrafficEvent.
/// Kept indefinitely (small, count-only, no visitor-level data) after raw
/// TrafficEvent rows older than 35 days are purged by the same job.
/// </summary>
public class TrafficDailyAggregate
{
    private TrafficDailyAggregate() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateOnly Date { get; private set; }
    public Guid? StudioId { get; private set; }
    public string? Role { get; private set; }
    public string? CountryCode { get; private set; }
    public int VisitCount { get; private set; }
    public int UniqueVisitorCount { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public static TrafficDailyAggregate Create(
        DateOnly date, Guid? studioId, string? role, string? countryCode,
        int visitCount, int uniqueVisitorCount) =>
        new()
        {
            Date = date,
            StudioId = studioId,
            Role = role,
            CountryCode = countryCode,
            VisitCount = visitCount,
            UniqueVisitorCount = uniqueVisitorCount,
        };

    public void UpdateCounts(int visitCount, int uniqueVisitorCount)
    {
        VisitCount = visitCount;
        UniqueVisitorCount = uniqueVisitorCount;
    }
}
