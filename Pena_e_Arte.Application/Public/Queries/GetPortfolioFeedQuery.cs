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

        // 1. Fetch all active artists with slugs. PortfolioImages uses a value converter
        //    and cannot be filtered in SQL — apply the count filter in-memory below.
        List<Artist> allArtists = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.DeletedAt == null && !string.IsNullOrEmpty(a.Slug))
            .ToListAsync(ct);

        List<Artist> artists = allArtists.Where(a => a.PortfolioImages.Count > 0).ToList();

        if (artists.Count == 0) return [];

        List<Guid> artistIds = artists.Select(a => a.Id).ToList();
        List<Guid> studioIds = artists.Select(a => a.StudioId).Distinct().ToList();

        // 2. Load studios (active only).
        Dictionary<Guid, Studio> studiosById = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => studioIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, ct);

        // 3. Artist-level review aggregates.
        Dictionary<Guid, (double Avg, int Count)> reviewStats = await db.Reviews
            .Where(r => r.ArtistId != null && artistIds.Contains(r.ArtistId.Value))
            .GroupBy(r => r.ArtistId!.Value)
            .Select(g => new { ArtistId = g.Key, Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.ArtistId, x => (x.Avg, x.Count), ct);

        // 4. View counts — batch MGET from Redis.
        IDatabase redisDb    = redis.GetDatabase();
        RedisKey[] redisKeys  = artistIds.Select(id => (RedisKey)$"portfolio:views:{id}").ToArray();
        RedisValue[] redisValues = await redisDb.StringGetAsync(redisKeys);
        Dictionary<Guid, long> viewCounts = artistIds
            .Zip(redisValues, (id, v) => (id, count: v.HasValue ? (long)v : 0L))
            .ToDictionary(x => x.id, x => x.count);

        // 5. Score artists.
        // Bayesian average: pulls low-count artists toward the global mean (3.5)
        // so one 5-star review does not outrank an artist with 50 genuine reviews.
        const double m = 5.0;   // minimum review threshold
        const double C = 3.5;   // prior mean (global average)

        static double BayesianScore(double avg, int count) =>
            (count * avg + m * C) / (count + m);

        // 6. Haversine for distance filter (in-memory; candidate set is already small).
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

        // 7. Score, filter by radius (if location provided), sort.
        List<(Artist Artist, Studio Studio, double? DistanceKm, double Score)> scored =
            artists
                .Where(a => studiosById.ContainsKey(a.StudioId))
                .Select(a =>
                {
                    Studio studio = studiosById[a.StudioId];

                    double? dist = (query.Lat.HasValue && query.Lng.HasValue)
                        ? Haversine(query.Lat.Value, query.Lng.Value, studio.Latitude, studio.Longitude)
                        : (double?)null;

                    bool inRange = !dist.HasValue || dist.Value <= query.RadiusKm;
                    return (Artist: a, Studio: studio, DistanceKm: dist, IsIncluded: inRange);
                })
                .Where(x => x.IsIncluded)
                .Select(x =>
                {
                    (double avg, int count) = reviewStats.GetValueOrDefault(x.Artist.Id, (0, 0));
                    long views = viewCounts.GetValueOrDefault(x.Artist.Id, 0L);
                    double score = BayesianScore(avg, count) + Math.Log10(views + 1) * 0.5;
                    return (x.Artist, x.Studio, x.DistanceKm, Score: score);
                })
                .OrderByDescending(x => x.Score)
                .ToList();

        // 8. Explode: take up to 3 images per artist, interleaved by artist rank.
        // Round-robin across artists so the feed doesn't cluster one artist's images together.
        // e.g. artist1-img1, artist2-img1, artist3-img1, artist1-img2, artist2-img2 ...
        const int maxImagesPerArtist = 3;
        List<List<PortfolioImageResponse>> columns = scored
            .Select(x =>
            {
                (double avg, int count) = reviewStats.GetValueOrDefault(x.Artist.Id, (0, 0));
                long views = viewCounts.GetValueOrDefault(x.Artist.Id, 0L);

                return x.Artist.PortfolioImages
                    .Take(maxImagesPerArtist)
                    .Select(url => new PortfolioImageResponse(
                        url,
                        $"{x.Artist.FirstName} {x.Artist.LastName}",
                        x.Artist.Slug!,
                        x.Studio.Name,
                        x.Studio.Slug,
                        count > 0 ? Math.Round(avg, 1) : null,
                        count,
                        x.DistanceKm.HasValue ? Math.Round(x.DistanceKm.Value, 1) : null,
                        views))
                    .ToList();
            })
            .ToList();

        // Interleave: take one image from each artist column in order.
        List<PortfolioImageResponse> interleaved = [];
        int maxImages = columns.Max(c => c.Count);
        for (int i = 0; i < maxImages; i++)
        {
            foreach (List<PortfolioImageResponse> col in columns)
            {
                if (i < col.Count) interleaved.Add(col[i]);
            }
        }

        int skip = (query.Page - 1) * query.PageSize;
        return interleaved.Skip(skip).Take(query.PageSize).ToList();
    }
}
