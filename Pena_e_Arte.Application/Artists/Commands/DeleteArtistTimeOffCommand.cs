using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record DeleteArtistTimeOffCommand(Guid ArtistId, Guid TimeOffId) : IRequest;

public class DeleteArtistTimeOffHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DeleteArtistTimeOffCommand>
{
    public async Task Handle(DeleteArtistTimeOffCommand command, CancellationToken ct)
    {
        var timeOff = await db.ArtistTimeOffs
            .FirstOrDefaultAsync(t => t.Id == command.TimeOffId && t.ArtistId == command.ArtistId, ct)
            ?? throw new NotFoundException("TimeOff", command.TimeOffId);

        if (currentUser.Role == "artist")
        {
            bool ownsArtist = await db.Artists
                .AnyAsync(a => a.Id == command.ArtistId && a.UserId == currentUser.UserId, ct);
            if (!ownsArtist)
                throw new ForbiddenException();
        }

        timeOff.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
