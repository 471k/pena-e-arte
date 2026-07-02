using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Instagram.Queries;

public record GetInstagramPostsQuery(Guid ArtistId, int Page = 1, int PageSize = 24)
    : IRequest<List<InstagramPostResponse>>;

public class GetInstagramPostsHandler(IAppDbContext db)
    : IRequestHandler<GetInstagramPostsQuery, List<InstagramPostResponse>>
{
    public async Task<List<InstagramPostResponse>> Handle(
        GetInstagramPostsQuery request, CancellationToken ct)
    {
        bool exists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!exists) throw new NotFoundException("Artist", request.ArtistId);

        return await db.InstagramPosts
            .Where(p => p.ArtistId == request.ArtistId)
            .OrderByDescending(p => p.PostedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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
