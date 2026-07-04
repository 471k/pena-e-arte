using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPortfolioImageReviewsQuery(Guid ImageId) : IRequest<List<ReviewResponse>>;

public class GetPortfolioImageReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetPortfolioImageReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetPortfolioImageReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — cross-tenant.
        Domain.Entities.PortfolioImage? image = await db.PortfolioImages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == query.ImageId, ct);

        if (image is null) return [];

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

        return await db.Reviews
            .Where(r => r.PortfolioImageId == query.ImageId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(
                r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt,
                verifiedUserIds.Contains(r.AuthorUserId),
                r.OwnerResponse, r.OwnerResponseAt))
            .ToListAsync(ct);
    }
}
