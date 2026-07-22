using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetClientProfileQuery(Guid ClientId) : IRequest<ClientProfileResponse?>;

public class GetClientProfileHandler(IAppDbContext db)
    : IRequestHandler<GetClientProfileQuery, ClientProfileResponse?>
{
    public async Task<ClientProfileResponse?> Handle(GetClientProfileQuery query, CancellationToken ct)
    {
        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(cp => cp.ClientId == query.ClientId, ct);

        return profile is null ? null : Map(profile);
    }

    internal static ClientProfileResponse Map(ClientProfile cp) =>
        new(cp.Id, cp.ClientId, cp.StudioId, cp.DateOfBirth,
            cp.MedicalNotes, cp.Allergies, cp.BodyMap.Locations, cp.UpdatedAt,
            cp.AllowCrossTenantRead);
}
