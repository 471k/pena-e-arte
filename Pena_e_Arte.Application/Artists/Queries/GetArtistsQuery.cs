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
        IQueryable<Artist> q = db.Artists
            .Include(a => a.Portfolio)
            .Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.ToLower();
            q = q.Where(a =>
                a.FirstName.ToLower().Contains(search) ||
                a.LastName.ToLower().Contains(search) ||
                a.Email.ToLower().Contains(search));
        }

        List<Artist> artists = await q
            .OrderBy(a => a.FirstName).ThenBy(a => a.LastName)
            .ToListAsync(ct);

        return artists
            .DistinctBy(a => a.Id)
            .Select(CreateArtistHandler.Map)
            .ToList();
    }
}
