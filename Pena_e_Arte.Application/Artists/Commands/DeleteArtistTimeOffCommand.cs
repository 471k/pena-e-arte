using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Artists.Commands;

public record DeleteArtistTimeOffCommand(Guid ArtistId, Guid TimeOffId) : IRequest;

public class DeleteArtistTimeOffHandler(IAppDbContext db)
    : IRequestHandler<DeleteArtistTimeOffCommand>
{
    public async Task Handle(DeleteArtistTimeOffCommand command, CancellationToken ct)
    {
        var timeOff = await db.ArtistTimeOffs
            .FirstOrDefaultAsync(t => t.Id == command.TimeOffId && t.ArtistId == command.ArtistId, ct)
            ?? throw new NotFoundException("TimeOff", command.TimeOffId);

        timeOff.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
