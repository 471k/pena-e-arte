using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdateMyBodyMapCommand(UpdateBodyMapRequest Request) : IRequest<ClientProfileResponse>;

public class UpdateMyBodyMapHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateMyBodyMapCommand, ClientProfileResponse>
{
    public async Task<ClientProfileResponse> Handle(UpdateMyBodyMapCommand command, CancellationToken ct)
    {
        Client? client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

        if (client is null)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(cp => cp.ClientId == client.Id, ct);

        if (profile is null)
            throw new NotFoundException(nameof(ClientProfile), client.Id);

        profile.BodyMap   = new BodyMap { Locations = command.Request.Locations };
        profile.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return GetClientProfileHandler.Map(profile);
    }
}
