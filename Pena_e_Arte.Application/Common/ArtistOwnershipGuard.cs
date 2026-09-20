using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// "An artist may act only on their own profile." The tenant query filter on db.Artists scopes a
/// caller to their studio, not to themselves — so an endpoint opened to the artist role (rather than
/// owner-only) must also pin an artist caller to their own Artist row, or any artist could act on a
/// colleague's profile in the same studio.
/// </summary>
public static class ArtistOwnershipGuard
{
    /// <summary>
    /// Throws ForbiddenException when the caller is an artist acting on someone else's profile.
    /// Owner and admin pass through unchecked — including a dual-role owner who is also an artist:
    /// their role claim is "owner", never "artist", so the check never runs for them. Call AFTER
    /// confirming the artist exists in the caller's tenant (a NotFound must win over a Forbidden so
    /// an artist can't probe ids from another studio).
    /// </summary>
    public static async Task EnsureCanActAsync(
        IAppDbContext db, ICurrentUser currentUser, Guid artistId, CancellationToken ct)
    {
        if (currentUser.Role != "artist") return;

        bool ownsProfile = await db.Artists
            .AnyAsync(a => a.Id == artistId && a.UserId == currentUser.UserId, ct);
        if (!ownsProfile) throw new ForbiddenException();
    }

    /// <summary>
    /// Social-link variant: only an Artist subject has an "owning artist". A Studio subject is
    /// reachable solely through owner-only endpoints, so there is nothing to pin an artist to.
    /// </summary>
    public static Task EnsureCanActOnSocialSubjectAsync(
        IAppDbContext db, ICurrentUser currentUser,
        SocialLinkSubjectType subjectType, Guid subjectId, CancellationToken ct) =>
        subjectType == SocialLinkSubjectType.Artist
            ? EnsureCanActAsync(db, currentUser, subjectId, ct)
            : Task.CompletedTask;
}
