using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Reads TrafficDailyAggregate — no IgnoreQueryFilters() needed, this entity carries no query
/// filter at all (see decision §2.2 / architecture.md), so this is a plain cross-tenant read.
/// Aggregated across all studios (platform-wide trend), grouped by day and guest-vs-role.
/// </summary>
public record GetTrafficHistoryQuery(int Days = 30) : IRequest<TrafficHistoryResponse>;

public class GetTrafficHistoryHandler(IAppDbContext db)
    : IRequestHandler<GetTrafficHistoryQuery, TrafficHistoryResponse>
{
    public async Task<TrafficHistoryResponse> Handle(GetTrafficHistoryQuery query, CancellationToken ct)
    {
        int days = Math.Clamp(query.Days, 1, 90);
        DateOnly since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

        List<TrafficDailyAggregate> rows = await db.TrafficDailyAggregates
            .AsNoTracking()
            .Where(t => t.Date >= since)
            .ToListAsync(ct);

        List<TrafficHistoryDataPoint> dataPoints = rows
            .GroupBy(r => r.Date)
            .OrderBy(g => g.Key)
            .Select(g => new TrafficHistoryDataPoint(
                Date: g.Key,
                GuestCount: g.Where(r => r.Role is null).Sum(r => r.VisitCount),
                ClientCount: g.Where(r => r.Role == "client").Sum(r => r.VisitCount),
                ArtistCount: g.Where(r => r.Role == "artist").Sum(r => r.VisitCount),
                OwnerCount: g.Where(r => r.Role == "owner").Sum(r => r.VisitCount),
                IssuerCount: g.Where(r => r.Role == "issuer").Sum(r => r.VisitCount)))
            .ToList();

        return new TrafficHistoryResponse(days, dataPoints);
    }
}
