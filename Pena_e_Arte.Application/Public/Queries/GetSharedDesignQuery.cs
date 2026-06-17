using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetSharedDesignQuery(string Token) : IRequest<SharedDesignResponse?>;

public class GetSharedDesignHandler(IAppDbContext db, IR2Service r2)
    : IRequestHandler<GetSharedDesignQuery, SharedDesignResponse?>
{
    public async Task<SharedDesignResponse?> Handle(GetSharedDesignQuery query, CancellationToken ct)
    {
        // Approved exception: design share token — public lookup, validated by token + expiry
        DesignShareToken? shareToken = await db.DesignShareTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == query.Token, ct);

        if (shareToken is null || shareToken.IsRevoked || shareToken.ExpiresAt < DateTime.UtcNow)
            return null;

        // Approved exception: design share token — public lookup, validated by token + expiry
        DesignRevision? revision = await db.DesignRevisions
            .IgnoreQueryFilters()
            .Include(r => r.Design)
            .FirstOrDefaultAsync(r => r.Id == shareToken.DesignRevisionId, ct);

        if (revision is null) return null;

        Studio? studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == shareToken.StudioId, ct);

        if (studio is null) return null;

        shareToken.ViewCount++;
        shareToken.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        string signedImageUrl = await r2.GeneratePresignedReadUrlAsync(revision.FileUrl, ct);

        return new SharedDesignResponse(
            signedImageUrl,
            revision.Design.Title,
            studio.Name,
            studio.Slug,
            shareToken.ExpiresAt);
    }
}
