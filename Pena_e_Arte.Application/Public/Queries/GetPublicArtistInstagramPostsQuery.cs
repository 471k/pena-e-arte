using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicArtistInstagramPostsQuery(string Slug) : IRequest<List<InstagramPostResponse>>;

public class GetPublicArtistInstagramPostsHandler(IAppDbContext db)
    : IRequestHandler<GetPublicArtistInstagramPostsQuery, List<InstagramPostResponse>>
{
    public async Task<List<InstagramPostResponse>> Handle(
        GetPublicArtistInstagramPostsQuery request, CancellationToken ct)
    {
        // Approved: public Instagram feed read — see architecture.md IgnoreQueryFilters entry 23.
        Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == request.Slug && a.DeletedAt == null, ct);

        if (artist is null) return [];

        // Mirrors GetPublicArtistQuery's Studio.IsActive check — a suspended studio's artist
        // must not leak Instagram posts here even though this is a separate public endpoint.
        bool studioActive = await db.Studios
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == artist.StudioId && s.IsActive, ct);

        if (!studioActive) return [];

        Guid artistId = artist.Id;

        return await db.InstagramPosts
            .Where(p => p.ArtistId == artistId && p.IsVisible)
            .OrderByDescending(p => p.PostedAt)
            .Take(24)
            .Select(p => new InstagramPostResponse(
                p.Id,
                p.InstagramMediaId,
                p.MediaUrl,
                p.ThumbnailUrl,
                p.Caption,
                p.MediaType,
                p.PostedAt,
                p.IsVisible))
            .ToListAsync(ct);
    }
}
