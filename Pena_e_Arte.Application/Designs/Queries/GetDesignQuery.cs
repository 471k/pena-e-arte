using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Designs;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignQuery(Guid Id) : IRequest<DesignResponse>;

public class GetDesignHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetDesignQuery, DesignResponse>
{
    public async Task<DesignResponse> Handle(GetDesignQuery query, CancellationToken ct)
    {
        Design design = await db.Designs
            .Include(d => d.Revisions).ThenInclude(r => r.Approval)
            .FirstOrDefaultAsync(d => d.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Design), query.Id);

        // Only the people the design list shows it to (DesignAccess); anyone else gets 404.
        if (!await DesignAccess.MayReadAsync(db, currentUser, design, ct))
            throw new NotFoundException(nameof(Design), query.Id);

        return GetDesignsHandler.Map(design);
    }
}
