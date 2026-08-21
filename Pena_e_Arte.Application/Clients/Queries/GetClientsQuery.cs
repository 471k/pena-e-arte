using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetClientsQuery(string? Search) : IRequest<List<ClientResponse>>;

public class GetClientsHandler(IAppDbContext db)
    : IRequestHandler<GetClientsQuery, List<ClientResponse>>
{
    public async Task<List<ClientResponse>> Handle(GetClientsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Client> q = db.Clients;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.ToLower();
            q = q.Where(c =>
                c.FirstName.ToLower().Contains(search) ||
                c.LastName.ToLower().Contains(search) ||
                c.Email.ToLower().Contains(search));
        }

        // Server-side projection, not .Include(c => c.Artist) + in-memory Map() — this list is
        // hit on every ClientListPage load and can run to hundreds/thousands of rows per
        // studio; only the columns ClientResponse actually needs are selected instead of
        // materializing full Client and Artist entities. ArtistId is the client's own scalar
        // FK (never masked by a soft-deleted artist). ArtistName's DeletedAt check is explicit
        // rather than relying on Artist's global query filter to apply inside a Select
        // projection — unlike .Include(), filter propagation into a projected navigation isn't
        // reliable, confirmed by a failing test without this check.
        return await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => new ClientResponse(
                c.Id, c.StudioId, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt, c.UserId,
                c.ArtistId,
                c.Artist != null && c.Artist.DeletedAt == null
                    ? c.Artist.FirstName + " " + c.Artist.LastName
                    : null))
            .ToListAsync(ct);
    }
}
