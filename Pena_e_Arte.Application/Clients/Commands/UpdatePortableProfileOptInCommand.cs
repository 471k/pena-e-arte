using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdatePortableProfileOptInCommand(UpdatePortableProfileOptInRequest Request)
    : IRequest<Unit>;

public class UpdatePortableProfileOptInHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdatePortableProfileOptInCommand, Unit>
{
    public async Task<Unit> Handle(UpdatePortableProfileOptInCommand command, CancellationToken ct)
    {
        Client? client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

        if (client is null)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(cp => cp.ClientId == client.Id, ct);

        if (profile is null)
        {
            // A brand-new client has no profile row yet — create one on first save
            // rather than blocking them, matching the owner-side upsert behaviour.
            profile = new ClientProfile { StudioId = client.StudioId, ClientId = client.Id };
            db.ClientProfiles.Add(profile);
        }

        if (command.Request.OptIn)
            profile.OptInToCrossTenant();
        else
            profile.OptOutOfCrossTenant();

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
