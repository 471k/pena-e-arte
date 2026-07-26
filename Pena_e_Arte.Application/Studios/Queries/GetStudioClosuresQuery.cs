using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.Application.Studios.Queries;

public record StudioClosureResponse(
    Guid Id,
    DateTime StartDate,
    DateTime EndDate,
    string Reason);

public record GetStudioClosuresQuery(Guid StudioId) : IRequest<List<StudioClosureResponse>>;

public class GetStudioClosuresHandler(IAppDbContext db)
    : IRequestHandler<GetStudioClosuresQuery, List<StudioClosureResponse>>
{
    public async Task<List<StudioClosureResponse>> Handle(
        GetStudioClosuresQuery query, CancellationToken ct)
    {
        return await db.StudioClosures
            .Where(c => c.EndDate >= DateTime.UtcNow.Date)
            .OrderBy(c => c.StartDate)
            .Select(c => new StudioClosureResponse(c.Id, c.StartDate, c.EndDate, c.Reason))
            .ToListAsync(ct);
    }
}
