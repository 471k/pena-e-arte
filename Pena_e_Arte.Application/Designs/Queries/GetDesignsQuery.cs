using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignsQuery(Guid? ClientId, Guid? ArtistId) : IRequest<List<DesignResponse>>;

public class GetDesignsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetDesignsQuery, List<DesignResponse>>
{
    public async Task<List<DesignResponse>> Handle(GetDesignsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Design> q = db.Designs;

        Guid? clientId = query.ClientId;
        if (currentUser.Role == "client")
        {
            Guid? myId = await db.Clients
                .Where(c => c.UserId == currentUser.UserId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (myId is null) return [];
            clientId = myId;
        }

        if (clientId.HasValue)      q = q.Where(d => d.ClientId == clientId.Value);
        if (query.ArtistId.HasValue) q = q.Where(d => d.ArtistId == query.ArtistId.Value);

        return await q
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => CreateDesignHandler.Map(d))
            .ToListAsync(ct);
    }
}
