using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Instagram.Commands;

public record DisconnectInstagramCommand(Guid ArtistId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.SocialDisconnected;
    public string AuditTargetType => AuditTargetTypes.Artist;
    public Guid AuditTargetId => ArtistId;
}

public class DisconnectInstagramHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DisconnectInstagramCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectInstagramCommand request, CancellationToken ct)
    {
        bool exists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!exists) throw new NotFoundException("Artist", request.ArtistId);

        await ArtistOwnershipGuard.EnsureCanActAsync(db, currentUser, request.ArtistId, ct);

        InstagramConnection? connection = await db.InstagramConnections
            .FirstOrDefaultAsync(c => c.ArtistId == request.ArtistId, ct);

        // Disconnect erases the stored (encrypted) access token, which lives on the connection
        // row, instead of keeping a dead-but-usable credential around. Reconnecting creates a
        // fresh row. Photos already synced deliberately stay on the portfolio (documented in Help).
        if (connection is not null)
            db.InstagramConnections.Remove(connection);

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
            socialLink.EncryptedToken = null;
            socialLink.TokenExpiresAt = null;
            socialLink.UpdatedAt = DateTime.UtcNow;
        }

        if (connection is not null || socialLink is not null)
            await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
