using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record RevokeDesignShareTokenCommand(Guid DesignShareTokenId) : IRequest<Unit>;

public class RevokeDesignShareTokenHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RevokeDesignShareTokenCommand, Unit>
{
    public async Task<Unit> Handle(RevokeDesignShareTokenCommand command, CancellationToken ct)
    {
        Domain.Entities.DesignShareToken? shareToken = await db.DesignShareTokens
            .FirstOrDefaultAsync(t => t.Id == command.DesignShareTokenId, ct);

        if (shareToken is null)
            throw new NotFoundException(nameof(Domain.Entities.DesignShareToken), command.DesignShareTokenId);

        if (currentUser.Role == "artist")
        {
            bool ownsDesign = await db.DesignRevisions
                .Where(r => r.Id == shareToken.DesignRevisionId)
                .Join(db.Designs, r => r.DesignId, d => d.Id, (r, d) => d.ArtistId)
                .Join(db.Artists, artistId => artistId, a => a.Id, (artistId, a) => a.UserId)
                .AnyAsync(userId => userId == currentUser.UserId, ct);
            if (!ownsDesign) throw new ForbiddenException();
        }

        shareToken.IsRevoked = true;
        shareToken.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
