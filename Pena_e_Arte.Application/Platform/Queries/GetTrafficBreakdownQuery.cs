using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Top countries come from TrafficDailyAggregate (retained indefinitely, has a CountryCode
/// dimension). Device/browser mix, top pages, and top networks have no equivalent aggregate
/// dimension — those come from raw TrafficEvent rows instead (kept 35 days by TrafficRollupJob's
/// purge, which is exactly why that retention window was chosen). Neither table carries a query
/// filter, so no IgnoreQueryFilters() is needed for either read (see decision §2.2 / architecture.md).
/// </summary>
public record GetTrafficBreakdownQuery(int Days = 30) : IRequest<TrafficBreakdownResponse>;

public class GetTrafficBreakdownHandler(IAppDbContext db)
    : IRequestHandler<GetTrafficBreakdownQuery, TrafficBreakdownResponse>
{
    private const int TopN = 10;

    public async Task<TrafficBreakdownResponse> Handle(GetTrafficBreakdownQuery query, CancellationToken ct)
    {
        int days = Math.Clamp(query.Days, 1, 90);
        DateOnly sinceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        DateTime sinceTimestamp = DateTime.UtcNow.AddDays(-days);

        // Projected into an anonymous type first, then mapped to the record client-side — EF
        // Core's InMemory provider (used by unit tests against FakeDbContext) cannot translate
        // a GroupBy aggregate straight into a record constructor inside Select the way the real
        // MySQL provider can; this form is what both providers accept. Each query is still fully
        // server-side aggregated (GROUP BY/COUNT or SUM translated to SQL) and capped to TopN at
        // the database — no raw row set is ever pulled into memory beyond the TopN result.
        var countrySums = await db.TrafficDailyAggregates
            .AsNoTracking()
            .Where(t => t.Date >= sinceDate && t.CountryCode != null)
            .GroupBy(t => t.CountryCode)
            .Select(g => new { CountryCode = g.Key, Count = g.Sum(t => t.VisitCount) })
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);
        List<TrafficCountryCount> topCountries = countrySums
            .Select(c => new TrafficCountryCount(c.CountryCode, null, c.Count))
            .ToList();

        List<TrafficNamedCount> deviceBreakdown = await TopNamedCountsAsync(
            db.TrafficEvents.Where(t => t.CreatedAt >= sinceTimestamp && t.DeviceType != null),
            t => t.DeviceType!, ct);

        List<TrafficNamedCount> browserBreakdown = await TopNamedCountsAsync(
            db.TrafficEvents.Where(t => t.CreatedAt >= sinceTimestamp && t.Browser != null),
            t => t.Browser!, ct);

        List<TrafficNamedCount> topPages = await TopNamedCountsAsync(
            db.TrafficEvents.Where(t => t.CreatedAt >= sinceTimestamp),
            t => t.Path, ct);

        // AsnOrganization (ISP/network) — free GeoLite2-ASN data, aggregate-only (never shown
        // per-visitor) since it identifies a network provider, not an individual.
        List<TrafficNamedCount> topNetworks = await TopNamedCountsAsync(
            db.TrafficEvents.Where(t => t.CreatedAt >= sinceTimestamp && t.AsnOrganization != null),
            t => t.AsnOrganization!, ct);

        return new TrafficBreakdownResponse(
            days, topCountries, deviceBreakdown, browserBreakdown, topPages, topNetworks);
    }

    private async Task<List<TrafficNamedCount>> TopNamedCountsAsync(
        IQueryable<TrafficEvent> source, Expression<Func<TrafficEvent, string>> keySelector, CancellationToken ct)
    {
        var counts = await source
            .GroupBy(keySelector)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        return counts.Select(c => new TrafficNamedCount(c.Name, c.Count)).ToList();
    }
}
