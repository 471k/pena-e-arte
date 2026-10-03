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

        // Disconnect erases everything we hold from Instagram: the stored (encrypted) access
        // token on the connection row, and the synced posts (the public portfolio reads those
        // without checking IsActive, so merely deactivating would leave them public). Reconnecting
        // creates a fresh row and the next sync repopulates the posts.
        List<InstagramPost> posts = await db.InstagramPosts
            .Where(p => p.ArtistId == request.ArtistId)
            .ToListAsync(ct);
        db.InstagramPosts.RemoveRange(posts);

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
            socialLink.AlternateExternalUserId = null;
            socialLink.EncryptedToken = null;
            socialLink.TokenExpiresAt = null;
            socialLink.UpdatedAt = DateTime.UtcNow;
        }

        if (connection is not null || socialLink is not null || posts.Count > 0)
            await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
