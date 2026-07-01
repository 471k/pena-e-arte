using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.Application.Saved.Queries;

public record GetSavedImageIdsQuery(Guid UserId) : IRequest<HashSet<Guid>>;

public class GetSavedImageIdsHandler(IAppDbContext db)
    : IRequestHandler<GetSavedImageIdsQuery, HashSet<Guid>>
{
    public async Task<HashSet<Guid>> Handle(GetSavedImageIdsQuery query, CancellationToken ct)
    {
        // Approved: user's saved image IDs — no tenant scope, keyed by UserId.
        return (await db.SavedPortfolioImages
            .Where(s => s.UserId == query.UserId)
            .Select(s => s.PortfolioImageId)
            .ToListAsync(ct))
            .ToHashSet();
    }
}
