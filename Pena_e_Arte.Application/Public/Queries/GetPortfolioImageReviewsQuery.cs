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
        // Approved: public review read, cross-tenant — see architecture.md IgnoreQueryFilters entry 21.
        // DeletedAt == null is a no-op today, kept for consistency with the artist lookup in
        // GetArtistReviewsQuery: nothing sets PortfolioImage.DeletedAt (removal is a hard RemoveRange in
        // UpdateArtistPortfolioCommand), so no soft-deleted image exists for it to exclude. It becomes
        // correct, not dead, the day portfolio images are soft-deleted. Deliberately untested: a test
        // would assert behaviour that cannot occur.
        Domain.Entities.PortfolioImage? image = await db.PortfolioImages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == query.ImageId && p.DeletedAt == null, ct);

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
