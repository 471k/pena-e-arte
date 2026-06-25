using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetArtistReviewsQuery(string Slug) : IRequest<List<ReviewResponse>>;

public class GetArtistReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetArtistReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetArtistReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return [];

        return await db.Reviews
            .Where(r => r.ArtistId == artist.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(ct);
    }
}
