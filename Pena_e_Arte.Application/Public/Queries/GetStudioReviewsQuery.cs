using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;

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

        return await db.Reviews
            .Where(r => r.StudioId == studio.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(
                r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt,
                verifiedUserIds.Contains(r.AuthorUserId),
                r.OwnerResponse, r.OwnerResponseAt))
            .ToListAsync(ct);
    }
}
