using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetNearbyStudiosQuery(double Lat, double Lng, double RadiusKm)
    : IRequest<List<NearbyStudioResponse>>;

public class GetNearbyStudiosHandler(IAppDbContext db)
    : IRequestHandler<GetNearbyStudiosQuery, List<NearbyStudioResponse>>
{
    public async Task<List<NearbyStudioResponse>> Handle(
        GetNearbyStudiosQuery query, CancellationToken ct)
    {
        // Bounding-box pre-filter then exact Haversine in memory.
        // Approved: public discovery query — see architecture.md AllowAnonymous Exceptions.
        double latDelta = query.RadiusKm / 111.0;
        double lngDelta = query.RadiusKm / (111.0 * Math.Cos(query.Lat * Math.PI / 180.0));

        List<Studio> candidates = await db.Studios
            .IgnoreQueryFilters()
            .Where(s =>
                s.IsActive && s.IsPublished &&
                s.Latitude >= query.Lat - latDelta && s.Latitude <= query.Lat + latDelta &&
                s.Longitude >= query.Lng - lngDelta && s.Longitude <= query.Lng + lngDelta)
            .ToListAsync(ct);

        // Count artists per studio (published, not deleted).
        // Approved: public discovery query.
        List<Guid> studioIds = candidates.Select(s => s.Id).ToList();
        Dictionary<Guid, int> artistCounts = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => studioIds.Contains(a.StudioId) && a.DeletedAt == null && a.Slug != null)
            .GroupBy(a => a.StudioId)
            .Select(g => new { StudioId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudioId, x => x.Count, ct);

        // Aggregate reviews per studio (Reviews has no global query filter — intentional).
        // Approved: public discovery query.
        Dictionary<Guid, (double Avg, int Count)> reviewStats = await db.Reviews
            .Where(r => r.StudioId != null && studioIds.Contains(r.StudioId!.Value))
            .GroupBy(r => r.StudioId!.Value)
            .Select(g => new
            {
                StudioId = g.Key,
                Avg = g.Average(r => (double)r.Rating),
                Count = g.Count(),
            })
            .ToDictionaryAsync(x => x.StudioId, x => (x.Avg, x.Count), ct);

        return candidates
            .Select(s => new
            {
                Studio = s,
                Distance = Haversine(query.Lat, query.Lng, s.Latitude, s.Longitude),
            })
            .Where(x => x.Distance <= query.RadiusKm)
            .OrderBy(x => x.Distance)
            .Take(40)
            .Select(x =>
            {
                (double avg, int count) = reviewStats.GetValueOrDefault(x.Studio.Id, (0, 0));
                return new NearbyStudioResponse(
                    x.Studio.Id,
                    x.Studio.Name,
                    x.Studio.Slug,
                    x.Studio.City,
                    x.Studio.CoverImageUrl,
                    Math.Round(x.Distance, 1),
                    artistCounts.GetValueOrDefault(x.Studio.Id, 0),
                    count > 0 ? Math.Round(avg, 1) : null,
                    count);
            })
            .ToList();
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
