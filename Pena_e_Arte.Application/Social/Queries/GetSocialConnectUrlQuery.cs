using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Social.Queries;

public record GetSocialConnectUrlQuery(SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform)
    : IRequest<SocialConnectUrlResponse>;

public class GetSocialConnectUrlHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ISocialOAuthProviderFactory providerFactory,
    ISocialOAuthStateSigner stateSigner)
    : IRequestHandler<GetSocialConnectUrlQuery, SocialConnectUrlResponse>
{
    public async Task<SocialConnectUrlResponse> Handle(GetSocialConnectUrlQuery request, CancellationToken ct)
    {
        if (request.SubjectType == SocialLinkSubjectType.Artist && request.Platform == SocialPlatform.Instagram)
            throw new BusinessRuleViolationException(
                "Use the artist's own Instagram connect flow (/artists/{id}/instagram/connect-url) instead.");

        await SocialSubjectResolver.ResolveStudioIdAsync(db, tenant, request.SubjectType, request.SubjectId, ct);

        ISocialOAuthProvider provider = providerFactory.GetProvider(request.Platform);
        if (!provider.IsConfigured)
            throw new ConflictException($"{request.Platform} isn't connected on this server yet.");

        string state = stateSigner.Sign(request.SubjectType, request.SubjectId, request.Platform);
        return new SocialConnectUrlResponse(provider.BuildAuthorizationUrl(state));
    }
}
