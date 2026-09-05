using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Commands;

public record VerifySocialBioCodeCommand(
    SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform)
    : IRequest<SocialVerifyResultResponse>;

public class VerifySocialBioCodeHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ISocialBioCheckerFactory checkerFactory,
    ILogger<VerifySocialBioCodeHandler> logger)
    : IRequestHandler<VerifySocialBioCodeCommand, SocialVerifyResultResponse>
{
    public async Task<SocialVerifyResultResponse> Handle(
        VerifySocialBioCodeCommand request, CancellationToken ct)
    {
        Guid studioId = await SocialSubjectResolver.ResolveStudioIdAsync(
            db, tenant, request.SubjectType, request.SubjectId, ct);

        ISocialBioChecker checker = checkerFactory.GetChecker(request.Platform);
        if (!checker.IsSupported)
            throw new BusinessRuleViolationException(
                $"{request.Platform} can't be verified this way — use Connect instead.");

        SocialAccountLink? link = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == request.SubjectType
              && s.SubjectId == request.SubjectId
              && s.Platform == request.Platform, ct);

        if (link?.PendingVerificationCode is null || link.PendingCodeExpiresAt is null)
            throw new BusinessRuleViolationException("Request a new code and try again.");

        if (link.PendingCodeExpiresAt < DateTime.UtcNow)
            throw new BusinessRuleViolationException("Your verification code has expired. Request a new code and try again.");

        bool found = await checker.BioContainsCodeAsync(link.Handle, link.PendingVerificationCode, ct);

        if (!found)
        {
            // Don't clear the pending code on a miss — the code may take a minute to
            // propagate on the platform's side; let the owner retry without re-requesting.
            return new SocialVerifyResultResponse(
                false, "We couldn't find that code in the bio yet. It can take a minute to update — try again shortly.");
        }

        link.IsVerified = true;
        link.VerifiedAt = DateTime.UtcNow;
        link.VerificationMethod = SocialVerificationMethod.ManualBioCode;
        link.PendingVerificationCode = null;
        link.PendingCodeExpiresAt = null;
        link.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Social link verified via manual bio code for studio {StudioId}, platform {Platform}",
            studioId, request.Platform);

        return new SocialVerifyResultResponse(true, null);
    }
}
