using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetClientQuery(Guid ClientId) : IRequest<ClientResponse>;

public class GetClientHandler(IAppDbContext db)
    : IRequestHandler<GetClientQuery, ClientResponse>
{
    public async Task<ClientResponse> Handle(GetClientQuery query, CancellationToken ct)
    {
        Domain.Entities.Client? client = await db.Clients
            .FirstOrDefaultAsync(c => c.Id == query.ClientId, ct);

        if (client is null)
            throw new NotFoundException(nameof(Domain.Entities.Client), query.ClientId);

        return CreateClientHandler.Map(client);
    }
}
