using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Instagram.Commands;

/// <summary>
/// ArtistId must already be authenticated by the caller (the API endpoint verifies
/// the signed OAuth `state` param via IInstagramStateSigner before dispatching this
/// command) — this handler trusts it and resolves the artist's StudioId itself,
/// since the OAuth callback is an anonymous redirect with no tenant context.
/// </summary>
public record ExchangeInstagramCodeCommand(Guid ArtistId, string Code) : IRequest<Unit>;

public class ExchangeInstagramCodeHandler(
    IAppDbContext db,
    IInstagramService instagram,
    ITokenEncryptor encryptor,
    ILogger<ExchangeInstagramCodeHandler> logger) : IRequestHandler<ExchangeInstagramCodeCommand, Unit>
{
    public async Task<Unit> Handle(ExchangeInstagramCodeCommand request, CancellationToken ct)
    {
        // Approved: anonymous OAuth callback — see architecture.md IgnoreQueryFilters entry 22.
        Guid studioId = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.Id == request.ArtistId && a.DeletedAt == null)
            .Select(a => a.StudioId)
            .FirstOrDefaultAsync(ct);

        if (studioId == Guid.Empty)
            throw new NotFoundException("Artist", request.ArtistId);

        InstagramTokenResponse tokenResponse = await instagram.ExchangeCodeAsync(request.Code, ct);
        string username = await instagram.GetUsernameAsync(tokenResponse.AccessToken, ct);
        string encryptedToken = encryptor.Encrypt(tokenResponse.AccessToken);
        DateTime expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        InstagramConnection? existing = await db.InstagramConnections
            .FirstOrDefaultAsync(c => c.ArtistId == request.ArtistId, ct);

        if (existing is null)
        {
            db.InstagramConnections.Add(new InstagramConnection
            {
                StudioId = studioId,
                ArtistId = request.ArtistId,
                InstagramUserId = tokenResponse.UserId,
                Username = username,
                EncryptedToken = encryptedToken,
                TokenExpiresAt = expiresAt,
                IsActive = true,
            });
        }
        else
        {
            existing.InstagramUserId = tokenResponse.UserId;
            existing.Username = username;
            existing.EncryptedToken = encryptedToken;
            existing.TokenExpiresAt = expiresAt;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        // SocialAccountLink is the one place every "Verified" badge reads from, across
        // every subject and platform — see docs/claude/architecture.md's Social
        // Verification entry. InstagramConnection above keeps owning the photo-sync
        // lifecycle exactly as before; this is purely additive.
        SocialAccountLink? socialLink = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == SocialLinkSubjectType.Artist
              && s.SubjectId == request.ArtistId
              && s.Platform == SocialPlatform.Instagram, ct);

        if (socialLink is null)
        {
            socialLink = new SocialAccountLink
            {
                StudioId = studioId,
                SubjectType = SocialLinkSubjectType.Artist,
                SubjectId = request.ArtistId,
                Platform = SocialPlatform.Instagram,
            };
            db.SocialAccountLinks.Add(socialLink);
        }

        socialLink.Handle = username;
        socialLink.IsVerified = true;
        socialLink.VerifiedAt = DateTime.UtcNow;
        socialLink.VerificationMethod = SocialVerificationMethod.OAuthConnect;
        socialLink.ExternalUserId = tokenResponse.UserId;
        // Kept (not discarded like the Studio-subject case) so a future periodic
        // re-verification job has a token to check — matches ExchangeSocialOAuthCodeCommand's
        // Artist-subject branch.
        socialLink.EncryptedToken = encryptedToken;
        socialLink.TokenExpiresAt = expiresAt;
        socialLink.PendingVerificationCode = null;
        socialLink.PendingCodeExpiresAt = null;
        socialLink.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Instagram connected for artist {ArtistId} in studio {StudioId}", request.ArtistId, studioId);

        return Unit.Value;
    }
}
