using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Instagram.Commands;

/// <summary>
/// Erases everything held for an Instagram user, triggered by Meta's Deauthorize or Data Deletion
/// callback. InstagramUserId must already be verified by the caller (the API endpoint checks Meta's
/// HMAC-signed request via IMetaSignedRequestParser before dispatching this command). Returns how
/// many connected accounts were erased; zero is a normal answer (already disconnected) and the
/// endpoints treat it exactly like a hit, so a caller can't probe which ids are connected.
/// </summary>
public record EraseInstagramDataCommand(string InstagramUserId) : IRequest<int>;

public class EraseInstagramDataHandler(IAppDbContext db, ILogger<EraseInstagramDataHandler> logger)
    : IRequestHandler<EraseInstagramDataCommand, int>
{
    private const string AuditMetadata = "{\"platform\":\"Instagram\",\"source\":\"meta-callback\"}";

    // InstagramConnection, InstagramPost and SocialAccountLink have no tenant query filter (see
    // AppDbContext), so this cross-tenant lookup needs no IgnoreQueryFilters() — the Instagram
    // user id is the key, not the caller's tenant (there is no caller context on this callback).
    public async Task<int> Handle(EraseInstagramDataCommand request, CancellationToken ct)
    {
        List<InstagramConnection> connections = await db.InstagramConnections
            .Where(c => c.InstagramUserId == request.InstagramUserId
                     || c.InstagramAccountId == request.InstagramUserId)
            .ToListAsync(ct);

        List<SocialAccountLink> links = await db.SocialAccountLinks
            .Where(s => s.Platform == SocialPlatform.Instagram
                     && (s.ExternalUserId == request.InstagramUserId
                         || s.AlternateExternalUserId == request.InstagramUserId))
            .ToListAsync(ct);

        List<Guid> artistIds = connections.Select(c => c.ArtistId)
            .Concat(links.Where(l => l.SubjectType == SocialLinkSubjectType.Artist).Select(l => l.SubjectId))
            .Distinct()
            .ToList();

        // The public portfolio reads synced posts without checking IsActive, so they must be
        // deleted, not just deactivated — same reasoning as DisconnectInstagramHandler.
        List<InstagramPost> posts = artistIds.Count == 0
            ? []
            : await db.InstagramPosts.Where(p => artistIds.Contains(p.ArtistId)).ToListAsync(ct);

        db.InstagramPosts.RemoveRange(posts);
        db.InstagramConnections.RemoveRange(connections);

        foreach (SocialAccountLink link in links)
        {
            // Clears verification only — keeps Handle, mirrors DisconnectInstagramHandler.
            link.IsVerified = false;
            link.VerifiedAt = null;
            link.VerificationMethod = null;
            link.ExternalUserId = null;
            link.AlternateExternalUserId = null;
            link.EncryptedToken = null;
            link.TokenExpiresAt = null;
            link.UpdatedAt = DateTime.UtcNow;

            db.AuditLogEntries.Add(AuditLogEntry.Create(
                actorUserId: Guid.Empty,
                actorRole: "meta-callback",
                action: AuditActions.SocialDisconnected,
                targetType: link.SubjectType == SocialLinkSubjectType.Artist
                    ? AuditTargetTypes.Artist
                    : AuditTargetTypes.Studio,
                targetId: link.SubjectId,
                studioId: link.StudioId,
                metadata: AuditMetadata));
        }

        // A connection with no matching link (older rows) still gets its own audit entry.
        foreach (InstagramConnection connection in connections
                     .Where(c => !links.Any(l => l.SubjectId == c.ArtistId)))
        {
            db.AuditLogEntries.Add(AuditLogEntry.Create(
                actorUserId: Guid.Empty,
                actorRole: "meta-callback",
                action: AuditActions.SocialDisconnected,
                targetType: AuditTargetTypes.Artist,
                targetId: connection.ArtistId,
                studioId: connection.StudioId,
                metadata: AuditMetadata));
        }

        int erased = connections.Select(c => c.ArtistId)
            .Concat(links.Select(l => l.SubjectId))
            .Distinct()
            .Count();

        if (erased > 0 || posts.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Meta sends Deauthorize and Data Deletion at the same moment when an app is removed, and
                // they can land on two replicas. Both load the rows, one deletes them first, and the other's
                // delete then affects 0 rows. The data is already erased (and audited) by the winner, so
                // answer like any already-disconnected user instead of a 500 Meta would treat as a failure.
                logger.LogInformation("Instagram data was already erased by a concurrent Meta callback");
                return 0;
            }

            logger.LogInformation(
                "Instagram data erased via Meta callback: {Accounts} account(s), {Posts} post(s)",
                erased, posts.Count);
        }

        else
        {
            // Zero is a normal answer (already disconnected), but it is also what an id-format mismatch
            // with Meta looks like, so record the shape — length and first 4 characters only, never the id.
            logger.LogInformation(
                "Instagram erase callback matched no account (id length {Length}, prefix {Prefix})",
                request.InstagramUserId.Length,
                request.InstagramUserId[..Math.Min(4, request.InstagramUserId.Length)]);
        }

        return erased;
    }
}
