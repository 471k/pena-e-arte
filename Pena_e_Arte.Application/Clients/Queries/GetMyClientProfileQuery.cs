using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetMyClientProfileQuery : IRequest<ClientProfileResponse>;

public class GetMyClientProfileHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyClientProfileQuery, ClientProfileResponse>
{
    public async Task<ClientProfileResponse> Handle(GetMyClientProfileQuery query, CancellationToken ct)
    {
        Client? client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

        if (client is null)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(cp => cp.ClientId == client.Id, ct);

        if (profile is null)
            throw new NotFoundException(nameof(ClientProfile), client.Id);

        return GetClientProfileHandler.Map(profile);
    }
}
