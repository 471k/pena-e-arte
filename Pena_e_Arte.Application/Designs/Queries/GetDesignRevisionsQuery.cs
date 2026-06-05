using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignRevisionsQuery(Guid DesignId) : IRequest<List<DesignRevisionResponse>>;

public class GetDesignRevisionsHandler(IAppDbContext db)
    : IRequestHandler<GetDesignRevisionsQuery, List<DesignRevisionResponse>>
{
    public async Task<List<DesignRevisionResponse>> Handle(GetDesignRevisionsQuery query, CancellationToken ct)
    {
        List<DesignRevision> revisions = await db.DesignRevisions
            .Where(r => r.DesignId == query.DesignId)
            .Include(r => r.Approval)
            .OrderBy(r => r.VersionNumber)
            .ToListAsync(ct);

        return revisions
            .Select(r => UploadDesignRevisionHandler.Map(r, r.Approval))
            .ToList();
    }
}
