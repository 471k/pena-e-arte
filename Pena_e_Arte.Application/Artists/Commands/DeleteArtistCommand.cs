using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Artists.Commands;

public record DeleteArtistCommand(Guid Id) : IRequest;

public class DeleteArtistHandler(IAppDbContext db)
    : IRequestHandler<DeleteArtistCommand>
{
    public async Task Handle(DeleteArtistCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        artist.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
