using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.StudioJoinInvites;

public record DeclineStudioJoinInviteCommand(Guid InviteId) : IRequest;

public class DeclineStudioJoinInviteHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DeclineStudioJoinInviteCommand>
{
    public async Task Handle(DeclineStudioJoinInviteCommand command, CancellationToken ct)
    {
        if (currentUser.Email is null)
            throw new NotFoundException(nameof(StudioJoinInvite), command.InviteId);

        // IgnoreQueryFilters: invites are cross-tenant by nature — see AppDbContext.
        // Plain == (not .ToLower()) — MySQL's default collation is already case-insensitive,
        // and .ToLower() on both sides would prevent the invited-email index from being used
        // (see ix_studio_join_invites_invited_email) for no behavioral benefit.
        StudioJoinInvite? invite = await db.StudioJoinInvites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i =>
                i.Id == command.InviteId
                && i.InvitedEmail == currentUser.Email, ct);

        if (invite is null || invite.Status != StudioJoinInviteStatus.Pending)
            throw new NotFoundException(nameof(StudioJoinInvite), command.InviteId);

        invite.Status = StudioJoinInviteStatus.Declined;
        invite.RespondedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
