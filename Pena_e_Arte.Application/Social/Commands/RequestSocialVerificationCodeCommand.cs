using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Commands;

public record RequestSocialVerificationCodeCommand(
    SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform)
    : IRequest<SocialVerificationCodeResponse>;

public class RequestSocialVerificationCodeHandler(
    IAppDbContext db, ICurrentTenant tenant, ISocialBioCheckerFactory checkerFactory)
    : IRequestHandler<RequestSocialVerificationCodeCommand, SocialVerificationCodeResponse>
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // excludes 0/O/1/I

    public async Task<SocialVerificationCodeResponse> Handle(
        RequestSocialVerificationCodeCommand request, CancellationToken ct)
    {
        Guid studioId = await SocialSubjectResolver.ResolveStudioIdAsync(
            db, tenant, request.SubjectType, request.SubjectId, ct);

        if (!checkerFactory.GetChecker(request.Platform).IsSupported)
            throw new BusinessRuleViolationException(
                $"{request.Platform} can't be verified this way — use Connect instead.");

        SocialAccountLink? link = await db.SocialAccountLinks.FirstOrDefaultAsync(
            s => s.SubjectType == request.SubjectType
              && s.SubjectId == request.SubjectId
              && s.Platform == request.Platform, ct);

        if (link is null || string.IsNullOrWhiteSpace(link.Handle))
            throw new BusinessRuleViolationException(
                "Set a handle for this platform before requesting a verification code.");

        string code = $"PENA-{GenerateCode()}";
        DateTime expiresAt = DateTime.UtcNow.AddHours(48);

        link.PendingVerificationCode = code;
        link.PendingCodeExpiresAt = expiresAt;
        link.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return new SocialVerificationCodeResponse(code, expiresAt);
    }

    private static string GenerateCode()
    {
        Span<char> chars = stackalloc char[6];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
        return new string(chars);
    }
}
