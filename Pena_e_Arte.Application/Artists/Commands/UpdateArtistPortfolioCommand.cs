using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record UpdateArtistPortfolioCommand(Guid Id, UpdateArtistPortfolioRequest Request) : IRequest<ArtistResponse>;

public class UpdateArtistPortfolioHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateArtistPortfolioCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(UpdateArtistPortfolioCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        // Artists can only update their own portfolio
        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        artist.PortfolioImages = command.Request.ImageUrls;
        artist.UpdatedAt       = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreateArtistHandler.Map(artist);
    }
}
