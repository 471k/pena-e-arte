using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Designs.Commands;

public record RevokeDesignShareTokenCommand(Guid DesignShareTokenId) : IRequest<Unit>;

public class RevokeDesignShareTokenHandler(IAppDbContext db)
    : IRequestHandler<RevokeDesignShareTokenCommand, Unit>
{
    public async Task<Unit> Handle(RevokeDesignShareTokenCommand command, CancellationToken ct)
    {
        Domain.Entities.DesignShareToken? shareToken = await db.DesignShareTokens
            .FirstOrDefaultAsync(t => t.Id == command.DesignShareTokenId, ct);

        if (shareToken is null)
            throw new NotFoundException(nameof(Domain.Entities.DesignShareToken), command.DesignShareTokenId);

        shareToken.IsRevoked = true;
        shareToken.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
