using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudioMapQuery : IRequest<List<StudioMapItemResponse>>;

public class GetStudioMapHandler(IAppDbContext db)
    : IRequestHandler<GetStudioMapQuery, List<StudioMapItemResponse>>
{
    public async Task<List<StudioMapItemResponse>> Handle(GetStudioMapQuery query, CancellationToken ct) =>
        await db.Studios
            .Where(s => s.IsActive)
            .Select(s => new StudioMapItemResponse(s.Id, s.Name, s.Slug, s.Latitude, s.Longitude, s.City))
            .ToListAsync(ct);
}
