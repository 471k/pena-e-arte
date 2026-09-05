using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Instagram.Commands;

public record DisconnectInstagramCommand(Guid ArtistId) : IRequest<Unit>;

public class DisconnectInstagramHandler(IAppDbContext db)
    : IRequestHandler<DisconnectInstagramCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectInstagramCommand request, CancellationToken ct)
    {
        bool exists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!exists) throw new NotFoundException("Artist", request.ArtistId);

        InstagramConnection? connection = await db.InstagramConnections
            .FirstOrDefaultAsync(c => c.ArtistId == request.ArtistId, ct);

        if (connection is not null)
        {
            connection.IsActive = false;
            connection.UpdatedAt = DateTime.UtcNow;
        }

        SocialAccountLink? socialLink = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == SocialLinkSubjectType.Artist
              && s.SubjectId == request.ArtistId
              && s.Platform == SocialPlatform.Instagram, ct);

        if (socialLink is not null)
        {
            // Clears verification only — keeps Handle, mirrors DisconnectSocialAccountCommand.
            socialLink.IsVerified = false;
            socialLink.VerifiedAt = null;
            socialLink.VerificationMethod = null;
            socialLink.ExternalUserId = null;
            socialLink.UpdatedAt = DateTime.UtcNow;
        }

        if (connection is not null || socialLink is not null)
            await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
