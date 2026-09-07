using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social;

/// <summary>
/// Resolves + verifies the real owning StudioId for a social-link subject.
/// SocialAccountLink carries no query filter (see its own doc comment), so every
/// handler in this feature must go through this rather than trusting
/// ICurrentTenant.StudioId directly:
/// - Artist subject: db.Artists carries the tenant query filter already, so a caller
///   can never resolve an artist outside their own studio through this path.
/// - Studio subject: Studio has no tenant filter at all (admin-level/unfiltered), so
///   this checks subjectId against ICurrentTenant.StudioId explicitly — the same
///   pattern UpdateStudioBrandingCommand/AddStudioClosureCommand/GenerateReferralCodeCommand
///   already use for OwnerOnly + route-studio-id endpoints elsewhere in this codebase.
/// </summary>
public static class SocialSubjectResolver
{
    public static async Task<Guid> ResolveStudioIdAsync(
        IAppDbContext db,
        ICurrentTenant tenant,
        SocialLinkSubjectType subjectType,
        Guid subjectId,
        CancellationToken ct)
    {
        if (subjectType == SocialLinkSubjectType.Artist)
        {
            Guid? studioId = await db.Artists
                .Where(a => a.Id == subjectId && a.DeletedAt == null)
                .Select(a => (Guid?)a.StudioId)
                .FirstOrDefaultAsync(ct);

            if (studioId is null) throw new NotFoundException("Artist", subjectId);
            return studioId.Value;
        }

        if (subjectId != tenant.StudioId)
            throw new NotFoundException(nameof(Studio), subjectId);

        return subjectId;
    }
}
