using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdateBodyMapCommand(Guid ClientId, UpdateBodyMapRequest Request)
    : IRequest<ClientProfileResponse>;

public class UpdateBodyMapHandler(IAppDbContext db)
    : IRequestHandler<UpdateBodyMapCommand, ClientProfileResponse>
{
    public async Task<ClientProfileResponse> Handle(UpdateBodyMapCommand command, CancellationToken ct)
    {
        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(cp => cp.ClientId == command.ClientId, ct);

        if (profile is null)
            throw new NotFoundException(nameof(ClientProfile), command.ClientId);

        profile.BodyMap = new BodyMap { Locations = command.Request.Locations };
        profile.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return GetClientProfileHandler.Map(profile);
    }
}
