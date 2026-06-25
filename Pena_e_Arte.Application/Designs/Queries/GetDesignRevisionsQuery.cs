using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignRevisionsQuery(Guid DesignId) : IRequest<List<DesignRevisionResponse>>;

public class GetDesignRevisionsHandler(IAppDbContext db, IR2Service r2)
    : IRequestHandler<GetDesignRevisionsQuery, List<DesignRevisionResponse>>
{
    private static readonly TimeSpan UrlTtl = TimeSpan.FromHours(1);

    public async Task<List<DesignRevisionResponse>> Handle(GetDesignRevisionsQuery query, CancellationToken ct)
    {
        List<DesignRevision> revisions = await db.DesignRevisions
            .Where(r => r.DesignId == query.DesignId)
            .Include(r => r.Approval)
            .OrderBy(r => r.VersionNumber)
            .ToListAsync(ct);

        List<DesignRevisionResponse> result = new(revisions.Count);
        foreach (DesignRevision rev in revisions)
        {
            string signedUrl = r2.IsR2Url(rev.FileUrl)
                ? await r2.GeneratePresignedReadUrlAsync(rev.FileUrl, UrlTtl, ct)
                : rev.FileUrl;
            result.Add(UploadDesignRevisionHandler.Map(rev, rev.Approval, signedUrl));
        }
        return result;
    }
}
