using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Commands;
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

        return await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => CreateClientHandler.Map(c))
            .ToListAsync(ct);
    }
}
