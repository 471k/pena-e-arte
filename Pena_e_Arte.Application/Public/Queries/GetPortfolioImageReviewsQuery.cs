using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPortfolioImageReviewsQuery(Guid ImageId) : IRequest<ReviewListResponse>;

public class GetPortfolioImageReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetPortfolioImageReviewsQuery, ReviewListResponse>
{
    public async Task<ReviewListResponse> Handle(
        GetPortfolioImageReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — cross-tenant.
        Domain.Entities.PortfolioImage? image = await db.PortfolioImages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == query.ImageId, ct);

        if (image is null) return new ReviewListResponse([], null, 0);

        // Approved: cross-tenant verified-booking check — see architecture.md IgnoreQueryFilters entry 21.
        HashSet<Guid> verifiedUserIds = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == image.StudioId && a.Status == AppointmentStatus.Completed)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => c.UserId)
            .Where(uid => uid != null)
            .Select(uid => uid!.Value)
            .Distinct()
            .ToHashSetAsync(ct);

        IQueryable<Domain.Entities.Review> reviews = db.Reviews.Where(r => r.PortfolioImageId == query.ImageId);

        List<ReviewResponse> newest = await reviews
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(
                r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt,
                verifiedUserIds.Contains(r.AuthorUserId),
                r.OwnerResponse, r.OwnerResponseAt))
            .ToListAsync(ct);

        // The summary covers every review, not just the newest 50 above, and uses the one display rule.
        var stats = await reviews
            .GroupBy(_ => 1)
            .Select(g => new { Sum = g.Sum(r => r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);
        int count = stats?.Count ?? 0;

        return new ReviewListResponse(newest, RatingDisplay.Average(stats?.Sum ?? 0, count), count);
    }
}
