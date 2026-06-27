using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Artists.Queries;

public record GetArtistsQuery(string? Search) : IRequest<List<ArtistResponse>>;

public class GetArtistsHandler(IAppDbContext db)
    : IRequestHandler<GetArtistsQuery, List<ArtistResponse>>
{
    public async Task<List<ArtistResponse>> Handle(GetArtistsQuery query, CancellationToken ct)
    {
        IQueryable<Artist> q = db.Artists.Include(a => a.Portfolio);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.ToLower();
            q = q.Where(a =>
                a.FirstName.ToLower().Contains(search) ||
                a.LastName.ToLower().Contains(search)  ||
                a.Email.ToLower().Contains(search));
        }

        List<Artist> artists = await q
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .ToListAsync(ct);

        return artists.ConvertAll(CreateArtistHandler.Map);
    }
}
