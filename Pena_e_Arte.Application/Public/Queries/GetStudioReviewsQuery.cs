using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetStudioReviewsQuery(string Slug) : IRequest<ReviewListResponse>;

public class GetStudioReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetStudioReviewsQuery, ReviewListResponse>
{
    public async Task<ReviewListResponse> Handle(
        GetStudioReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return new ReviewListResponse([], null, 0);

        // Approved: cross-tenant verified-booking check — see architecture.md IgnoreQueryFilters entry 20.
        HashSet<Guid> verifiedUserIds = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == studio.Id && a.Status == AppointmentStatus.Completed)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => c.UserId)
            .Where(uid => uid != null)
            .Select(uid => uid!.Value)
            .Distinct()
            .ToHashSetAsync(ct);

        IQueryable<Domain.Entities.Review> reviews = db.Reviews.Where(r => r.StudioId == studio.Id);

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
