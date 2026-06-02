using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignsQuery(Guid? ClientId, Guid? ArtistId) : IRequest<List<DesignResponse>>;

public class GetDesignsHandler(IAppDbContext db)
    : IRequestHandler<GetDesignsQuery, List<DesignResponse>>
{
    public async Task<List<DesignResponse>> Handle(GetDesignsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Design> q = db.Designs;

        if (query.ClientId.HasValue) q = q.Where(d => d.ClientId == query.ClientId.Value);
        if (query.ArtistId.HasValue) q = q.Where(d => d.ArtistId == query.ArtistId.Value);

        return await q
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => CreateDesignHandler.Map(d))
            .ToListAsync(ct);
    }
}
