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

        // No profile row yet is a normal state for a new client, not an error — the
        // client hasn't been asked to fill anything in, or no staff member has
        // recorded medical notes yet. Return sensible empty defaults so the client's
        // own profile screen (body map, sharing toggle) works from day one.
        if (profile is null)
            return new ClientProfileResponse(
                Guid.Empty, client.Id, client.StudioId,
                DateOfBirth: null, MedicalNotes: null, Allergies: null,
                BodyMapLocations: [], UpdatedAt: client.CreatedAt, AllowCrossTenantRead: false);

        return GetClientProfileHandler.Map(profile);
    }
}
