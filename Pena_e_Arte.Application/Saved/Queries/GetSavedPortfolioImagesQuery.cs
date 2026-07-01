using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Saved.Queries;

public record GetSavedPortfolioImagesQuery(Guid UserId, int Page = 1, int PageSize = 24)
    : IRequest<List<PortfolioImageResponse>>;

public class GetSavedPortfolioImagesHandler(IAppDbContext db)
    : IRequestHandler<GetSavedPortfolioImagesQuery, List<PortfolioImageResponse>>
{
    public async Task<List<PortfolioImageResponse>> Handle(
        GetSavedPortfolioImagesQuery query, CancellationToken ct)
    {
        // Approved: cross-tenant — user may have saved images from any studio.
        // IgnoreQueryFilters() bypasses tenant filters on PortfolioImage and Artist in the include chain.
        List<SavedPortfolioImage> saved = await db.SavedPortfolioImages
            .Where(s => s.UserId == query.UserId)
            .OrderByDescending(s => s.SavedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(s => s.PortfolioImage)
                .ThenInclude(p => p.Artist)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        if (saved.Count == 0) return [];

        List<Guid> studioIds = saved.Select(s => s.PortfolioImage.Artist.StudioId).Distinct().ToList();

        Dictionary<Guid, Studio> studiosById = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => studioIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, ct);

        return saved
            .Where(s => studiosById.ContainsKey(s.PortfolioImage.Artist.StudioId))
            .Select(s =>
            {
                PortfolioImage img    = s.PortfolioImage;
                Artist         artist = img.Artist;
                Studio         studio = studiosById[artist.StudioId];

                return new PortfolioImageResponse(
                    ImageId:            img.Id,
                    ImageUrl:           img.ImageUrl,
                    Style:              img.Style,
                    ArtistName:         $"{artist.FirstName} {artist.LastName}".Trim(),
                    ArtistSlug:         artist.Slug!,
                    StudioName:         studio.Name,
                    StudioSlug:         studio.Slug,
                    AverageRating:      null,
                    ReviewCount:        0,
                    ImageAverageRating: null,
                    ImageReviewCount:   0,
                    DistanceKm:         null,
                    ViewCount:          0L);
            })
            .ToList();
    }
}
