using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Designs;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignRevisionsQuery(Guid DesignId) : IRequest<List<DesignRevisionResponse>>;

public class GetDesignRevisionsHandler(IAppDbContext db, IR2Service r2, ICurrentUser currentUser)
    : IRequestHandler<GetDesignRevisionsQuery, List<DesignRevisionResponse>>
{
    private static readonly TimeSpan UrlTtl = TimeSpan.FromHours(1);

    public async Task<List<DesignRevisionResponse>> Handle(GetDesignRevisionsQuery query, CancellationToken ct)
    {
        // Only the people the design list shows it to (DesignAccess); anyone else gets 404, including for
        // a design id that doesn't exist (previously that returned an empty list).
        Design design = await db.Designs.FirstOrDefaultAsync(d => d.Id == query.DesignId, ct)
            ?? throw new NotFoundException(nameof(Design), query.DesignId);
        if (!await DesignAccess.MayReadAsync(db, currentUser, design, ct))
            throw new NotFoundException(nameof(Design), query.DesignId);

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
