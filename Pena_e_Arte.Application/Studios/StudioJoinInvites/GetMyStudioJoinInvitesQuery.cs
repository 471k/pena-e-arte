using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.StudioJoinInvites;

public record GetMyStudioJoinInvitesQuery : IRequest<List<MyStudioJoinInviteResponse>>;

public class GetMyStudioJoinInvitesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyStudioJoinInvitesQuery, List<MyStudioJoinInviteResponse>>
{
    public async Task<List<MyStudioJoinInviteResponse>> Handle(
        GetMyStudioJoinInvitesQuery query, CancellationToken ct)
    {
        if (currentUser.Email is null) return [];

        DateTime now = DateTime.UtcNow;

        // IgnoreQueryFilters: invites are cross-tenant by nature — see AppDbContext.
        // Plain == (not .ToLower()) — MySQL's default collation is already case-insensitive,
        // and .ToLower() on both sides would prevent the invited-email index from being used
        // (see ix_studio_join_invites_invited_email) for no behavioral benefit.
        // Studio.IsActive excludes an invite from a studio that's since been suspended —
        // otherwise it would still show as "pending" and be acceptable despite the inviting
        // studio being unusable.
        List<StudioJoinInvite> invites = await db.StudioJoinInvites.IgnoreQueryFilters()
            .Include(i => i.Studio)
            .Where(i => i.InvitedEmail == currentUser.Email
                        && i.Status == StudioJoinInviteStatus.Pending
                        && i.ExpiresAt > now
                        && i.Studio.IsActive)
            .ToListAsync(ct);

        return invites
            .Select(i => new MyStudioJoinInviteResponse(
                i.Id, i.StudioId, i.Studio.Name, i.Studio.Slug, i.Studio.City, i.ExpiresAt))
            .ToList();
    }
}
