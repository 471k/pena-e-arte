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

public class GetTrafficBreakdownHandler(IAppDbContextFactory dbContextFactory)
    : IRequestHandler<GetTrafficBreakdownQuery, TrafficBreakdownResponse>
{
    private const int TopN = 10;

    public async Task<TrafficBreakdownResponse> Handle(GetTrafficBreakdownQuery query, CancellationToken ct)
    {
        int days = Math.Clamp(query.Days, 1, 90);
        DateOnly sinceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        DateTime sinceTimestamp = DateTime.UtcNow.AddDays(-days);

        // These 5 aggregate reads are independent (different GROUP BY key, no shared state) but
        // a single EF Core DbContext can't serve overlapping operations — so each branch pulls
        // its own short-lived context from the factory and they run concurrently via
        // Task.WhenAll instead of the 5 sequential round-trips this used to be.
        Task<List<TrafficCountryCount>> countriesTask = TopCountriesAsync(sinceDate, ct);
        Task<List<TrafficNamedCount>> deviceTask = TopNamedCountsAsync(
            t => t.CreatedAt >= sinceTimestamp && t.DeviceType != null, t => t.DeviceType!, ct);
        Task<List<TrafficNamedCount>> browserTask = TopNamedCountsAsync(
            t => t.CreatedAt >= sinceTimestamp && t.Browser != null, t => t.Browser!, ct);
        Task<List<TrafficNamedCount>> pagesTask = TopNamedCountsAsync(
            t => t.CreatedAt >= sinceTimestamp, t => t.Path, ct);
        // AsnOrganization (ISP/network) — free GeoLite2-ASN data, aggregate-only (never shown
        // per-visitor) since it identifies a network provider, not an individual.
        Task<List<TrafficNamedCount>> networksTask = TopNamedCountsAsync(
            t => t.CreatedAt >= sinceTimestamp && t.AsnOrganization != null, t => t.AsnOrganization!, ct);

        await Task.WhenAll(countriesTask, deviceTask, browserTask, pagesTask, networksTask);

        return new TrafficBreakdownResponse(
            days, countriesTask.Result, deviceTask.Result, browserTask.Result, pagesTask.Result, networksTask.Result);
    }

    private async Task<List<TrafficCountryCount>> TopCountriesAsync(DateOnly sinceDate, CancellationToken ct)
    {
        await using IAppDbContextLease lease = await dbContextFactory.CreateDbContextAsync(ct);
        IAppDbContext db = lease.Context;

        // Projected into an anonymous type first, then mapped to the record client-side — EF
        // Core's InMemory provider (used by unit tests against FakeDbContext) cannot translate
        // a GroupBy aggregate straight into a record constructor inside Select the way the real
        // MySQL provider can; this form is what both providers accept. Still fully server-side
        // aggregated (GROUP BY/SUM translated to SQL) and capped to TopN at the database.
        var countrySums = await db.TrafficDailyAggregates
            .AsNoTracking()
            .Where(t => t.Date >= sinceDate && t.CountryCode != null)
            .GroupBy(t => t.CountryCode)
            .Select(g => new { CountryCode = g.Key, Count = g.Sum(t => t.VisitCount) })
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        return countrySums.Select(c => new TrafficCountryCount(c.CountryCode, null, c.Count)).ToList();
    }

    private async Task<List<TrafficNamedCount>> TopNamedCountsAsync(
        Expression<Func<TrafficEvent, bool>> predicate,
        Expression<Func<TrafficEvent, string>> keySelector,
        CancellationToken ct)
    {
        await using IAppDbContextLease lease = await dbContextFactory.CreateDbContextAsync(ct);
        IAppDbContext db = lease.Context;

        var counts = await db.TrafficEvents
            .Where(predicate)
            .GroupBy(keySelector)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .Take(TopN)
            .ToListAsync(ct);

        return counts.Select(c => new TrafficNamedCount(c.Name, c.Count)).ToList();
    }
}
