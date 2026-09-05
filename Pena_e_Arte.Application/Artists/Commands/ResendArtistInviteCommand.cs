using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record ResendArtistInviteCommand(Guid Id) : IRequest;

public class ResendArtistInviteHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IJobScheduler scheduler,
    IIdentityService identity)
    : IRequestHandler<ResendArtistInviteCommand>
{
    public async Task Handle(ResendArtistInviteCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        // The owner's own linked artist profile (see CreateOwnArtistProfileCommand) was never
        // created via an invite email — there's nothing to resend, and sending one would be a
        // confusing "set your password" email to someone who already has full credentials.
        if (artist.UserId is not null)
        {
            IReadOnlyList<string> roles = await identity.GetUserRolesAsync(artist.UserId.Value, ct);
            if (roles.Contains("owner"))
                throw new BusinessRuleViolationException(
                    "This artist profile belongs to the studio owner's own account — there is no invite to resend.");
        }

        scheduler.EnqueueArtistInvite(artist.Email, artist.FirstName, tenant.StudioId);
    }
}
