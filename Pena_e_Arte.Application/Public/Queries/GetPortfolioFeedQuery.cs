using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using StackExchange.Redis;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPortfolioFeedQuery(
    double? Lat,
    double? Lng,
    double  RadiusKm,
    int     Page,
    int     PageSize = 24) : IRequest<List<PortfolioImageResponse>>;

public class GetPortfolioFeedHandler(IAppDbContext db, IConnectionMultiplexer redis)
    : IRequestHandler<GetPortfolioFeedQuery, List<PortfolioImageResponse>>
{
    public async Task<List<PortfolioImageResponse>> Handle(
        GetPortfolioFeedQuery query, CancellationToken ct)
    {
        // Approved: public portfolio discovery — no tenant scope required.
        // All IgnoreQueryFilters calls below are intentional (cross-tenant public data).

        static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        // 1. Optional: resolve studio IDs within radius.
        HashSet<Guid>? filteredStudioIds = null;
        if (query.Lat.HasValue && query.Lng.HasValue)
        {
            List<(Guid Id, double Lat, double Lng)> allStudios = await db.Studios
                .IgnoreQueryFilters()
                .Where(s => s.IsActive)
                .Select(s => new { s.Id, s.Latitude, s.Longitude })
                .ToListAsync(ct)
                .ContinueWith(t => t.Result.Select(s => (s.Id, s.Latitude, s.Longitude)).ToList(),
                    TaskContinuationOptions.ExecuteSynchronously);

            filteredStudioIds = allStudios
                .Where(s => Haversine(query.Lat.Value, query.Lng.Value, s.Lat, s.Lng) <= query.RadiusKm)
                .Select(s => s.Id)
                .ToHashSet();
        }

        // 2. Load portfolio images with artist.
        IQueryable<PortfolioImage> imageQuery = db.PortfolioImages
            .IgnoreQueryFilters()
            .Include(p => p.Artist)
            .Where(p => p.Artist.DeletedAt == null && !string.IsNullOrEmpty(p.Artist.Slug));

        if (filteredStudioIds is not null)
            imageQuery = imageQuery.Where(p => filteredStudioIds.Contains(p.StudioId));

        List<PortfolioImage> images = await imageQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        if (images.Count == 0) return [];

        List<Guid> imageIds  = images.Select(p => p.Id).ToList();
        List<Guid> artistIds = images.Select(p => p.ArtistId).Distinct().ToList();

        // 3. Per-image review aggregates.
        Dictionary<Guid, (double Sum, int Count)> imageReviews = await db.Reviews
            .Where(r => r.PortfolioImageId.HasValue && imageIds.Contains(r.PortfolioImageId!.Value))
            .GroupBy(r => r.PortfolioImageId!.Value)
            .Select(g => new { Id = g.Key, Sum = g.Sum(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => (x.Sum, x.Count), ct);

        // 4. Artist-level review aggregates.
        Dictionary<Guid, (double Sum, int Count)> artistReviews = await db.Reviews
            .Where(r => r.ArtistId.HasValue && artistIds.Contains(r.ArtistId!.Value))
            .GroupBy(r => r.ArtistId!.Value)
            .Select(g => new { Id = g.Key, Sum = g.Sum(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => (x.Sum, x.Count), ct);

        // 5. Redis view counts — batch MGET.
        IDatabase redisDb    = redis.GetDatabase();
        RedisKey[] redisKeys  = artistIds.Select(id => (RedisKey)$"portfolio:views:{id}").ToArray();
        RedisValue[] redisValues = await redisDb.StringGetAsync(redisKeys);
        Dictionary<Guid, long> viewCounts = artistIds
            .Zip(redisValues, (id, v) => (id, count: v.HasValue ? (long)v : 0L))
            .ToDictionary(x => x.id, x => x.count);

        // 6. Studios for the artists on this page.
        List<Guid> studioIds = images.Select(p => p.Artist.StudioId).Distinct().ToList();
        Dictionary<Guid, Studio> studiosById = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => studioIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, ct);

        // 7. Score, sort, project.
        // Bayesian blend: 60% image rating, 40% artist rating when both have reviews.
        return images
            .Where(img => studiosById.ContainsKey(img.Artist.StudioId))
            .Select(img =>
            {
                imageReviews.TryGetValue(img.Id, out (double Sum, int Count) ir);
                artistReviews.TryGetValue(img.ArtistId, out (double Sum, int Count) ar);
                viewCounts.TryGetValue(img.ArtistId, out long views);

                double imageAvg  = ir.Count > 0 ? ir.Sum / ir.Count : 3.5;
                double artistAvg = ar.Count > 0 ? ar.Sum / ar.Count : 3.5;
                double blended   = ir.Count > 0 ? imageAvg * 0.6 + artistAvg * 0.4 : artistAvg;
                double bayesian  = (ir.Count * blended + 5 * 3.5) / (ir.Count + 5);
                double score     = bayesian * 0.7 + Math.Log10(views + 1) * 0.3;

                return (Image: img, Score: score, Ir: ir, Ar: ar, Views: views);
            })
            .OrderByDescending(x => x.Score)
            .Select(x =>
            {
                Artist a      = x.Image.Artist;
                Studio studio = studiosById[a.StudioId];

                double? distKm = (query.Lat.HasValue && query.Lng.HasValue)
                    ? Math.Round(Haversine(query.Lat.Value, query.Lng.Value, studio.Latitude, studio.Longitude), 1)
                    : null;

                return new PortfolioImageResponse(
                    ImageId:            x.Image.Id,
                    ImageUrl:           x.Image.ImageUrl,
                    ArtistName:         $"{a.FirstName} {a.LastName}".Trim(),
                    ArtistSlug:         a.Slug!,
                    StudioName:         studio.Name,
                    StudioSlug:         studio.Slug,
                    AverageRating:      x.Ar.Count > 0 ? Math.Round(x.Ar.Sum / x.Ar.Count, 1) : null,
                    ReviewCount:        x.Ar.Count,
                    ImageAverageRating: x.Ir.Count > 0 ? Math.Round(x.Ir.Sum / x.Ir.Count, 1) : null,
                    ImageReviewCount:   x.Ir.Count,
                    DistanceKm:         distKm,
                    ViewCount:          x.Views);
            })
            .ToList();
    }
}
