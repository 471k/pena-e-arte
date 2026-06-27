using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPortfolioImageReviewsQuery(Guid ImageId) : IRequest<List<ReviewResponse>>;

public class GetPortfolioImageReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetPortfolioImageReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetPortfolioImageReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — cross-tenant.
        bool imageExists = await db.PortfolioImages
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == query.ImageId, ct);

        if (!imageExists) return [];

        return await db.Reviews
            .Where(r => r.PortfolioImageId == query.ImageId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(ct);
    }
}
