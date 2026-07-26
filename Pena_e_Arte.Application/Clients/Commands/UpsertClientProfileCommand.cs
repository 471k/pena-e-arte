using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpsertClientProfileCommand(Guid ClientId, UpsertClientProfileRequest Request)
    : IRequest<ClientProfileResponse>;

public class UpsertClientProfileHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpsertClientProfileCommand, ClientProfileResponse>
{
    public async Task<ClientProfileResponse> Handle(UpsertClientProfileCommand command, CancellationToken ct)
    {
        bool clientExists = await db.Clients.AnyAsync(c => c.Id == command.ClientId, ct);
        if (!clientExists)
            throw new NotFoundException(nameof(Client), command.ClientId);

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(cp => cp.ClientId == command.ClientId, ct);

        UpsertClientProfileRequest req = command.Request;

        if (profile is null)
        {
            profile = new ClientProfile
            {
                StudioId = tenant.StudioId,
                ClientId = command.ClientId,
                DateOfBirth = req.DateOfBirth,
                MedicalNotes = req.MedicalNotes,
                Allergies = req.Allergies,
            };
            db.ClientProfiles.Add(profile);
        }
        else
        {
            profile.DateOfBirth = req.DateOfBirth;
            profile.MedicalNotes = req.MedicalNotes;
            profile.Allergies = req.Allergies;
            profile.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return GetClientProfileHandler.Map(profile);
    }
}
