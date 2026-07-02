using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignQuery(Guid Id) : IRequest<DesignResponse>;

public class GetDesignHandler(IAppDbContext db)
    : IRequestHandler<GetDesignQuery, DesignResponse>
{
    public async Task<DesignResponse> Handle(GetDesignQuery query, CancellationToken ct)
    {
        Design design = await db.Designs
            .Include(d => d.Revisions).ThenInclude(r => r.Approval)
            .FirstOrDefaultAsync(d => d.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Design), query.Id);

        return GetDesignsHandler.Map(design);
    }
}
