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
    IAppDbContext  db,
    ICurrentTenant tenant,
    ICurrentUser   currentUser)
    : IRequestHandler<CreateDesignShareTokenCommand, DesignShareTokenResponse>
{
    public async Task<DesignShareTokenResponse> Handle(CreateDesignShareTokenCommand command, CancellationToken ct)
    {
        bool revisionExists = await db.DesignRevisions
            .AnyAsync(r => r.Id == command.DesignRevisionId, ct);

        if (!revisionExists)
            throw new NotFoundException(nameof(DesignRevision), command.DesignRevisionId);

        DesignShareToken shareToken = new()
        {
            StudioId         = tenant.StudioId,
            Token            = Guid.NewGuid().ToString("N"),
            DesignRevisionId = command.DesignRevisionId,
            CreatedByUserId  = currentUser.UserId,
            ExpiresAt        = DateTime.UtcNow.AddDays(30)
        };

        db.DesignShareTokens.Add(shareToken);
        await db.SaveChangesAsync(ct);

        string shareUrl = $"https://penaearte.com/share/{shareToken.Token}";

        return new DesignShareTokenResponse(shareToken.Id, shareToken.Token, shareUrl, shareToken.ExpiresAt);
    }
}
