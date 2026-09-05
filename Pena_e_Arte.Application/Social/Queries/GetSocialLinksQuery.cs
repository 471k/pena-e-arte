using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Queries;

public record GetSocialLinksQuery(SocialLinkSubjectType SubjectType, Guid SubjectId)
    : IRequest<List<SocialLinkStatusResponse>>;

public class GetSocialLinksHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ISocialOAuthProviderFactory providerFactory,
    ISocialBioCheckerFactory checkerFactory)
    : IRequestHandler<GetSocialLinksQuery, List<SocialLinkStatusResponse>>
{
    public async Task<List<SocialLinkStatusResponse>> Handle(GetSocialLinksQuery request, CancellationToken ct)
    {
        await SocialSubjectResolver.ResolveStudioIdAsync(db, tenant, request.SubjectType, request.SubjectId, ct);

        Dictionary<SocialPlatform, SocialAccountLink> existing = await db.SocialAccountLinks
            .Where(s => s.SubjectType == request.SubjectType && s.SubjectId == request.SubjectId)
            .ToDictionaryAsync(s => s.Platform, ct);

        return Enum.GetValues<SocialPlatform>()
            .OrderBy(p => (int)p)
            .Select(platform =>
            {
                existing.TryGetValue(platform, out SocialAccountLink? link);

                return new SocialLinkStatusResponse(
                    platform.ToString(),
                    link?.Handle,
                    link?.IsVerified ?? false,
                    link?.VerifiedAt,
                    link?.VerificationMethod?.ToString(),
                    providerFactory.GetProvider(platform).IsConfigured,
                    checkerFactory.GetChecker(platform).IsSupported,
                    link?.PendingVerificationCode is not null,
                    link?.PendingCodeExpiresAt);
            })
            .ToList();
    }
}
