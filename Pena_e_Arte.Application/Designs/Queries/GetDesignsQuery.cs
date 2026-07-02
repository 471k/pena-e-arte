using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Queries;

public record GetDesignsQuery(Guid? ClientId, Guid? ArtistId) : IRequest<List<DesignResponse>>;

public class GetDesignsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetDesignsQuery, List<DesignResponse>>
{
    public async Task<List<DesignResponse>> Handle(GetDesignsQuery query, CancellationToken ct)
    {
        IQueryable<Design> q = db.Designs
            .Include(d => d.Revisions).ThenInclude(r => r.Approval);

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

        Guid? artistId = query.ArtistId;
        if (currentUser.Role == "artist")
        {
            // An artist can only ever see their own designs — a requested artistId for
            // someone else is ignored rather than trusted (their GUID could be guessed).
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null) return [];
            artistId = myArtistId;
        }

        if (clientId.HasValue) q = q.Where(d => d.ClientId == clientId.Value);
        if (artistId.HasValue) q = q.Where(d => d.ArtistId == artistId.Value);

        List<Design> designs = await q
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return designs.Select(Map).ToList();
    }

    internal static DesignResponse Map(Design d) =>
        new(d.Id, d.StudioId, d.ClientId, d.ArtistId, d.Title, d.Description, d.CreatedAt, ComputeStatus(d));

    // A design has no status of its own — it's derived from its latest revision's
    // approval. Expired approvals are treated the same as ChangesRequested since both
    // mean the artist needs to upload a new revision next.
    internal static string ComputeStatus(Design d)
    {
        DesignRevision? latest = d.Revisions.OrderByDescending(r => r.VersionNumber).FirstOrDefault();
        if (latest is null) return "Draft";

        return latest.Approval?.Status switch
        {
            null                                    => "InReview",
            DesignApprovalStatus.Pending             => "InReview",
            DesignApprovalStatus.Approved            => "Approved",
            DesignApprovalStatus.ChangesRequested     => "ChangesRequested",
            DesignApprovalStatus.Expired              => "ChangesRequested",
            _                                          => "InReview",
        };
    }
}
