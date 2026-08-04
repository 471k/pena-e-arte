using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Top countries come from TrafficDailyAggregate (retained indefinitely, has a CountryCode
/// dimension). Device/browser mix and top pages have no equivalent aggregate dimension — those
/// come from raw TrafficEvent rows instead (kept 35 days by TrafficRollupJob's purge, which is
/// exactly why that retention window was chosen). Neither table carries a query filter, so no
/// IgnoreQueryFilters() is needed for either read (see decision §2.2 / architecture.md).
/// </summary>
public record GetTrafficBreakdownQuery(int Days = 30) : IRequest<TrafficBreakdownResponse>;

public class GetTrafficBreakdownHandler(IAppDbContext db)
    : IRequestHandler<GetTrafficBreakdownQuery, TrafficBreakdownResponse>
{
    private const int TopN = 10;

    public async Task<TrafficBreakdownResponse> Handle(GetTrafficBreakdownQuery query, CancellationToken ct)
    {
        DateOnly sinceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-query.Days));
        DateTime sinceTimestamp = DateTime.UtcNow.AddDays(-query.Days);

        List<TrafficCountryCount> topCountries = await db.TrafficDailyAggregates
            .AsNoTracking()
            .Where(t => t.Date >= sinceDate && t.CountryCode != null)
            .GroupBy(t => t.CountryCode)
            .Select(g => new TrafficCountryCount(g.Key, null, g.Sum(t => t.VisitCount)))
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        // Each aggregated server-side (GROUP BY/COUNT translated to SQL) and capped to TopN at
        // the database — no raw TrafficEvent row set is ever pulled into memory for this.
        List<TrafficNamedCount> deviceBreakdown = await db.TrafficEvents
            .Where(t => t.CreatedAt >= sinceTimestamp && t.DeviceType != null)
            .GroupBy(t => t.DeviceType!)
            .Select(g => new TrafficNamedCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        List<TrafficNamedCount> browserBreakdown = await db.TrafficEvents
            .Where(t => t.CreatedAt >= sinceTimestamp && t.Browser != null)
            .GroupBy(t => t.Browser!)
            .Select(g => new TrafficNamedCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        List<TrafficNamedCount> topPages = await db.TrafficEvents
            .Where(t => t.CreatedAt >= sinceTimestamp)
            .GroupBy(t => t.Path)
            .Select(g => new TrafficNamedCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        return new TrafficBreakdownResponse(query.Days, topCountries, deviceBreakdown, browserBreakdown, topPages);
    }
}
