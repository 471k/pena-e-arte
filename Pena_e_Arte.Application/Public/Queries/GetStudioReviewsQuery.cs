using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetStudioReviewsQuery(string Slug) : IRequest<List<ReviewResponse>>;

public class GetStudioReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetStudioReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetStudioReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return [];

        return await db.Reviews
            .Where(r => r.StudioId == studio.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(ct);
    }
}
