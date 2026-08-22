using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetMyClientQuery : IRequest<ClientResponse>;

public class GetMyClientHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyClientQuery, ClientResponse>
{
    public async Task<ClientResponse> Handle(GetMyClientQuery query, CancellationToken ct)
    {
        Client? client = await db.Clients
            .Include(c => c.Artist)
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

        if (client is null)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        return CreateClientHandler.Map(client, client.Artist);
    }
}
