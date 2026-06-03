using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Artists.Queries;

public record GetArtistsQuery(string? Search) : IRequest<List<ArtistResponse>>;

public class GetArtistsHandler(IAppDbContext db)
    : IRequestHandler<GetArtistsQuery, List<ArtistResponse>>
{
    public async Task<List<ArtistResponse>> Handle(GetArtistsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Artist> q = db.Artists;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.ToLower();
            q = q.Where(a =>
                a.FirstName.ToLower().Contains(search) ||
                a.LastName.ToLower().Contains(search)  ||
                a.Email.ToLower().Contains(search));
        }

        return await q
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => CreateArtistHandler.Map(a))
            .ToListAsync(ct);
    }
}
