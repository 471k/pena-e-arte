using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Queries;

public record GetMyArtistQuery : IRequest<ArtistResponse>;

public class GetMyArtistHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyArtistQuery, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(GetMyArtistQuery query, CancellationToken ct)
    {
        Artist? artist = await db.Artists
            .Include(a => a.Portfolio)
            .FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct);

        if (artist is null)
            throw new NotFoundException(nameof(Artist), currentUser.UserId);

        return CreateArtistHandler.Map(artist);
    }
}
