using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record ResendArtistInviteCommand(Guid Id) : IRequest;

public class ResendArtistInviteHandler(
    IAppDbContext  db,
    ICurrentTenant tenant,
    IJobScheduler  scheduler)
    : IRequestHandler<ResendArtistInviteCommand>
{
    public async Task Handle(ResendArtistInviteCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        scheduler.EnqueueArtistInvite(artist.Email, artist.FirstName, tenant.StudioId);
    }
}
