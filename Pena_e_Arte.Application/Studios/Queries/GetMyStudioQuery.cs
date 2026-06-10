using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetMyStudioQuery : IRequest<StudioResponse>;

public class GetMyStudioHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetMyStudioQuery, StudioResponse>
{
    public async Task<StudioResponse> Handle(GetMyStudioQuery query, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            studio.TrialExpiresAt, studio.CreatedAt);
    }
}
