using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Artists.Queries;

public record GetArtistQuery(Guid Id) : IRequest<ArtistResponse>;

public class GetArtistHandler(IAppDbContext db)
    : IRequestHandler<GetArtistQuery, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(GetArtistQuery query, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == query.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), query.Id);

        return CreateArtistHandler.Map(artist);
    }
}
