using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudioByIdQuery(Guid StudioId) : IRequest<StudioResponse>;

public class GetStudioByIdHandler(IAppDbContext db)
    : IRequestHandler<GetStudioByIdQuery, StudioResponse>
{
    public async Task<StudioResponse> Handle(GetStudioByIdQuery query, CancellationToken ct)
    {
        // IssuerOnly endpoint — IgnoreQueryFilters approved: usage #8 (cross-tenant read).
        // See architecture.md Approved Usages table.
        StudioResponse? studio = await db.Studios
            .IgnoreQueryFilters()
            .Select(s => new StudioResponse(
                s.Id, s.Name, s.Slug, s.City,
                s.Latitude, s.Longitude,
                s.ShowPlatformBranding,
                AllowBrandingRemoval: false,
                s.TrialExpiresAt, s.CreatedAt, s.IsActive))
            .FirstOrDefaultAsync(s => s.Id == query.StudioId, ct);

        if (studio is null)
            throw new NotFoundException("Studio", query.StudioId);

        return studio;
    }
}
