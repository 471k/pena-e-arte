using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Commands;

/// <summary>
/// SubjectType/SubjectId/Platform must already be authenticated by the caller (the API
/// endpoint verifies the signed OAuth `state` param via ISocialOAuthStateSigner before
/// dispatching this command) — this handler trusts it and resolves the subject's real
/// StudioId itself, since the OAuth callback is an anonymous redirect with no tenant
/// context. Never dispatched for (Artist, Instagram) — that combination stays on
/// ExchangeInstagramCodeCommand, see its own hook into SocialAccountLink.
/// </summary>
public record ExchangeSocialOAuthCodeCommand(
    SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform, string Code)
    : IRequest<Unit>;

public class ExchangeSocialOAuthCodeHandler(
    IAppDbContext db,
    ISocialOAuthProviderFactory providerFactory,
    ITokenEncryptor encryptor,
    ILogger<ExchangeSocialOAuthCodeHandler> logger)
    : IRequestHandler<ExchangeSocialOAuthCodeCommand, Unit>
{
    public async Task<Unit> Handle(ExchangeSocialOAuthCodeCommand request, CancellationToken ct)
    {
        Guid studioId;

        if (request.SubjectType == SocialLinkSubjectType.Artist)
        {
            // Approved: anonymous OAuth callback — same exception class as
            // ExchangeInstagramCodeCommand's own IgnoreQueryFilters use.
            studioId = await db.Artists
                .IgnoreQueryFilters()
                .Where(a => a.Id == request.SubjectId && a.DeletedAt == null)
                .Select(a => a.StudioId)
                .FirstOrDefaultAsync(ct);

            if (studioId == Guid.Empty) throw new NotFoundException("Artist", request.SubjectId);
        }
        else
        {
            studioId = request.SubjectId;
        }

        // Suspended-studio check — the exact class of bug already fixed twice for the
        // artist Instagram path (see architecture.md Decisions Log); do it here from
        // day one instead of waiting for a third bug report.
        bool studioActive = await db.Studios
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == studioId && s.IsActive, ct);
        if (!studioActive) throw new NotFoundException(nameof(Studio), studioId);

        ISocialOAuthProvider provider = providerFactory.GetProvider(request.Platform);
        if (!provider.IsConfigured)
            throw new ConflictException($"{request.Platform} isn't connected on this server yet.");

        SocialOAuthTokenResponse token = await provider.ExchangeCodeAsync(request.Code, ct);
        string username = await provider.GetUsernameAsync(token.AccessToken, ct);

        SocialAccountLink? link = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == request.SubjectType
              && s.SubjectId == request.SubjectId
              && s.Platform == request.Platform, ct);

        if (link is null)
        {
            link = new SocialAccountLink
            {
                StudioId = studioId,
                SubjectType = request.SubjectType,
                SubjectId = request.SubjectId,
                Platform = request.Platform,
            };
            db.SocialAccountLinks.Add(link);
        }

        link.Handle = username;
        link.IsVerified = true;
        link.VerifiedAt = DateTime.UtcNow;
        link.VerificationMethod = SocialVerificationMethod.OAuthConnect;
        link.ExternalUserId = token.ExternalUserId;
        link.PendingVerificationCode = null;
        link.PendingCodeExpiresAt = null;
        link.UpdatedAt = DateTime.UtcNow;

        if (request.SubjectType == SocialLinkSubjectType.Studio)
        {
            // No ongoing sync need for a studio — this is a one-time identity check, so
            // the token is discarded immediately rather than persisted/encrypted.
            link.EncryptedToken = null;
            link.TokenExpiresAt = null;
        }
        else
        {
            link.EncryptedToken = encryptor.Encrypt(token.AccessToken);
            link.TokenExpiresAt = token.ExpiresAt;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Social OAuth connected for studio {StudioId}, subject {SubjectType}, platform {Platform}",
            studioId, request.SubjectType, request.Platform);

        return Unit.Value;
    }
}
