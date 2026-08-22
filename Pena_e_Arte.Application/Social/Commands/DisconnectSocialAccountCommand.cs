using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Commands;

public record DisconnectSocialAccountCommand(SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform)
    : IRequest<Unit>;

public class DisconnectSocialAccountHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<DisconnectSocialAccountCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectSocialAccountCommand request, CancellationToken ct)
    {
        await SocialSubjectResolver.ResolveStudioIdAsync(db, tenant, request.SubjectType, request.SubjectId, ct);

        SocialAccountLink? link = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == request.SubjectType
              && s.SubjectId == request.SubjectId
              && s.Platform == request.Platform, ct);

        if (link is not null)
        {
            // Clears verification only — keeps Handle so the owner doesn't lose their
            // last-known handle display, mirrors DisconnectInstagramCommand's
            // IsActive=false-not-delete approach for the analogous InstagramConnection row.
            link.IsVerified = false;
            link.VerifiedAt = null;
            link.VerificationMethod = null;
            link.ExternalUserId = null;
            link.EncryptedToken = null;
            link.TokenExpiresAt = null;
            link.PendingVerificationCode = null;
            link.PendingCodeExpiresAt = null;
            link.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
