using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record CreateDesignShareTokenCommand(Guid DesignRevisionId) : IRequest<DesignShareTokenResponse>;

public class CreateDesignShareTokenHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser)
    : IRequestHandler<CreateDesignShareTokenCommand, DesignShareTokenResponse>
{
    public async Task<DesignShareTokenResponse> Handle(CreateDesignShareTokenCommand command, CancellationToken ct)
    {
        DesignRevision? revision = await db.DesignRevisions
            .FirstOrDefaultAsync(r => r.Id == command.DesignRevisionId, ct);

        if (revision is null)
            throw new NotFoundException(nameof(DesignRevision), command.DesignRevisionId);

        if (currentUser.Role == "artist")
        {
            bool ownsDesign = await db.Designs
                .Join(db.Artists, d => d.ArtistId, a => a.Id, (d, a) => new { d.Id, a.UserId })
                .AnyAsync(x => x.Id == revision.DesignId && x.UserId == currentUser.UserId, ct);
            if (!ownsDesign) throw new ForbiddenException();
        }

        DesignShareToken? active = await db.DesignShareTokens
            .Where(t => t.DesignRevisionId == command.DesignRevisionId
                     && !t.IsRevoked
                     && t.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        if (active is null)
        {
            active = new DesignShareToken
            {
                StudioId = tenant.StudioId,
                Token = Guid.NewGuid().ToString("N"),
                DesignRevisionId = command.DesignRevisionId,
                CreatedByUserId = currentUser.UserId,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };

            db.DesignShareTokens.Add(active);
            await db.SaveChangesAsync(ct);
        }

        string shareUrl = $"https://tattooos.co/share/{active.Token}";

        return new DesignShareTokenResponse(active.Id, active.Token, shareUrl, active.ExpiresAt);
    }
}
