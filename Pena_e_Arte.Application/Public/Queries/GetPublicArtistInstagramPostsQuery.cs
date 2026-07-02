using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicArtistInstagramPostsQuery(string Slug) : IRequest<List<InstagramPostResponse>>;

public class GetPublicArtistInstagramPostsHandler(IAppDbContext db)
    : IRequestHandler<GetPublicArtistInstagramPostsQuery, List<InstagramPostResponse>>
{
    public async Task<List<InstagramPostResponse>> Handle(
        GetPublicArtistInstagramPostsQuery request, CancellationToken ct)
    {
        // Approved: public Instagram feed read — see architecture.md IgnoreQueryFilters entry 23.
        Guid? artistId = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.Slug == request.Slug && a.DeletedAt == null)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (artistId is null) return [];

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
