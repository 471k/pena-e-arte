using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudiosQuery : IRequest<List<StudioResponse>>;

public class GetStudiosHandler(IAppDbContext db)
    : IRequestHandler<GetStudiosQuery, List<StudioResponse>>
{
    public async Task<List<StudioResponse>> Handle(GetStudiosQuery query, CancellationToken ct)
    {
        return await db.Studios
            .OrderBy(s => s.Name)
            .Select(s => new StudioResponse(
                s.Id, s.Name, s.Slug, s.City,
                s.Latitude, s.Longitude,
                s.ShowPlatformBranding,
                AllowBrandingRemoval: false,
                s.TrialExpiresAt, s.CreatedAt))
            .ToListAsync(ct);
    }
}
