using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdatePortableProfileOptInCommand(UpdatePortableProfileOptInRequest Request)
    : IRequest<Unit>, IAuditableCommand
{
    // The profile is resolved from ICurrentUser inside the handler, so the command carries
    // no id at construction. The handler sets this before returning; AuditLogBehavior reads
    // AuditTargetId AFTER the handler completes, so it sees the resolved value.
    public Guid ResolvedProfileId { get; set; }

    public string AuditAction => Request.OptIn
        ? AuditActions.ClientProfileCrossTenantOptedIn
        : AuditActions.ClientProfileCrossTenantOptedOut;
    public string AuditTargetType => AuditTargetTypes.ClientProfile;
    public Guid AuditTargetId => ResolvedProfileId;
}

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
        {
            // Reuse the versioned-consent-with-snapshot pattern for the cross-tenant
            // profile-sharing consent — resolve the active template server-side and
            // snapshot its text so the profile records exactly what was agreed to.
            // (The shared read model is tattoo history + body map, not medical data —
            // see ConsentTemplateKind.CrossTenantProfileSharing.)
            List<ConsentTemplate> candidates = await db.ConsentTemplates
                .Where(t => t.Kind == ConsentTemplateKind.CrossTenantProfileSharing
                            && t.IsActive
                            && (t.StudioId == client.StudioId || t.StudioId == null))
                .ToListAsync(ct);

            ConsentTemplate? template = ConsentTemplateResolver.ResolveActive(
                candidates, client.StudioId, ConsentTemplateKind.CrossTenantProfileSharing,
                DateTime.UtcNow);

            profile.OptInToCrossTenant(template?.Id, template?.BodyText);
        }
        else
        {
            profile.OptOutOfCrossTenant();
        }

        command.ResolvedProfileId = profile.Id;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
