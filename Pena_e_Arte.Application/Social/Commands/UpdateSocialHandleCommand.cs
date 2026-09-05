using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Commands;

/// <summary>
/// Sets/updates the display handle an owner has typed for a platform — needed before
/// the manual bio-code flow can check anything (the checker needs to know which
/// profile to look at), and also used to correct a handle before reconnecting via
/// OAuth. Changing the handle on an already-verified link un-verifies it: the old
/// verification proved ownership of the *previous* handle, not this one.
/// </summary>
public record UpdateSocialHandleCommand(
    SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform, string Handle)
    : IRequest<Unit>;

public class UpdateSocialHandleHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateSocialHandleCommand, Unit>
{
    public async Task<Unit> Handle(UpdateSocialHandleCommand request, CancellationToken ct)
    {
        Guid studioId = await SocialSubjectResolver.ResolveStudioIdAsync(
            db, tenant, request.SubjectType, request.SubjectId, ct);

        string handle = request.Handle.TrimStart('@').Trim();

        SocialAccountLink? link = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == request.SubjectType
              && s.SubjectId == request.SubjectId
              && s.Platform == request.Platform, ct);

        if (link is null)
        {
            db.SocialAccountLinks.Add(new SocialAccountLink
            {
                StudioId = studioId,
                SubjectType = request.SubjectType,
                SubjectId = request.SubjectId,
                Platform = request.Platform,
                Handle = handle,
            });
        }
        else if (!string.Equals(link.Handle, handle, StringComparison.Ordinal))
        {
            link.Handle = handle;
            link.IsVerified = false;
            link.VerifiedAt = null;
            link.VerificationMethod = null;
            link.PendingVerificationCode = null;
            link.PendingCodeExpiresAt = null;
            link.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
