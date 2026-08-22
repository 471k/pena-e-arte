using MediatR;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Application.Social.Queries;
using Pena_e_Arte.Contracts.Requests.Social;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.API.Endpoints;

public static class SocialEndpoints
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder artistGroup = app.MapGroup("/api/v1/artists/{id:guid}/social")
            .RequireAuthorization();

        artistGroup.MapGet("/", (Guid id, ISender m, CancellationToken ct) =>
            GetLinks(SocialLinkSubjectType.Artist, id, m, ct)).RequireAuthorization("ArtistAndAbove");
        artistGroup.MapGet("/{platform}/connect-url", (Guid id, string platform, ISender m, CancellationToken ct) =>
            GetConnectUrl(SocialLinkSubjectType.Artist, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
        artistGroup.MapPut("/{platform}/handle", (Guid id, string platform, UpdateSocialHandleRequest req, ISender m, CancellationToken ct) =>
            UpdateHandle(SocialLinkSubjectType.Artist, id, platform, req, m, ct)).RequireAuthorization("OwnerOnly");
        artistGroup.MapPost("/{platform}/request-code", (Guid id, string platform, ISender m, CancellationToken ct) =>
            RequestCode(SocialLinkSubjectType.Artist, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
        artistGroup.MapPost("/{platform}/verify-code", (Guid id, string platform, ISender m, CancellationToken ct) =>
            VerifyCode(SocialLinkSubjectType.Artist, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
        artistGroup.MapDelete("/{platform}/disconnect", (Guid id, string platform, ISender m, CancellationToken ct) =>
            Disconnect(SocialLinkSubjectType.Artist, id, platform, m, ct)).RequireAuthorization("OwnerOnly");

        RouteGroupBuilder studioGroup = app.MapGroup("/api/v1/studios/{id:guid}/social")
            .RequireAuthorization();

        studioGroup.MapGet("/", (Guid id, ISender m, CancellationToken ct) =>
            GetLinks(SocialLinkSubjectType.Studio, id, m, ct)).RequireAuthorization("OwnerOnly");
        studioGroup.MapGet("/{platform}/connect-url", (Guid id, string platform, ISender m, CancellationToken ct) =>
            GetConnectUrl(SocialLinkSubjectType.Studio, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
        studioGroup.MapPut("/{platform}/handle", (Guid id, string platform, UpdateSocialHandleRequest req, ISender m, CancellationToken ct) =>
            UpdateHandle(SocialLinkSubjectType.Studio, id, platform, req, m, ct)).RequireAuthorization("OwnerOnly");
        studioGroup.MapPost("/{platform}/request-code", (Guid id, string platform, ISender m, CancellationToken ct) =>
            RequestCode(SocialLinkSubjectType.Studio, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
        studioGroup.MapPost("/{platform}/verify-code", (Guid id, string platform, ISender m, CancellationToken ct) =>
            VerifyCode(SocialLinkSubjectType.Studio, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
        studioGroup.MapDelete("/{platform}/disconnect", (Guid id, string platform, ISender m, CancellationToken ct) =>
            Disconnect(SocialLinkSubjectType.Studio, id, platform, m, ct)).RequireAuthorization("OwnerOnly");
    }

    /// <summary>
    /// Public OAuth callback shared by TikTok/Facebook/X/YouTube and studio-Instagram —
    /// artist-Instagram keeps using the existing /api/v1/instagram/callback endpoint
    /// unchanged (see ExchangeInstagramCodeCommand). Not authenticated; the signed
    /// `state` param is what's trusted, not the caller or the {platform} route value.
    /// </summary>
    public static void MapSocialCallbackEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/social/{platform}/callback", HandleCallback)
            .AllowAnonymous()
            .RequireRateLimiting("public-write");
    }

    private static bool TryParsePlatform(string raw, out SocialPlatform platform) =>
        Enum.TryParse(raw, ignoreCase: true, out platform) && Enum.IsDefined(platform);

    private static async Task<IResult> GetLinks(
        SocialLinkSubjectType subjectType, Guid id, ISender mediator, CancellationToken ct)
    {
        List<SocialLinkStatusResponse> result =
            await mediator.Send(new GetSocialLinksQuery(subjectType, id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetConnectUrl(
        SocialLinkSubjectType subjectType, Guid id, string platformRaw, ISender mediator, CancellationToken ct)
    {
        if (!TryParsePlatform(platformRaw, out SocialPlatform platform))
            return Results.BadRequest("Unrecognized platform.");

        SocialConnectUrlResponse result =
            await mediator.Send(new GetSocialConnectUrlQuery(subjectType, id, platform), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateHandle(
        SocialLinkSubjectType subjectType, Guid id, string platformRaw,
        UpdateSocialHandleRequest request, ISender mediator, CancellationToken ct)
    {
        if (!TryParsePlatform(platformRaw, out SocialPlatform platform))
            return Results.BadRequest("Unrecognized platform.");

        await mediator.Send(new UpdateSocialHandleCommand(subjectType, id, platform, request.Handle), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestCode(
        SocialLinkSubjectType subjectType, Guid id, string platformRaw, ISender mediator, CancellationToken ct)
    {
        if (!TryParsePlatform(platformRaw, out SocialPlatform platform))
            return Results.BadRequest("Unrecognized platform.");

        SocialVerificationCodeResponse result =
            await mediator.Send(new RequestSocialVerificationCodeCommand(subjectType, id, platform), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> VerifyCode(
        SocialLinkSubjectType subjectType, Guid id, string platformRaw, ISender mediator, CancellationToken ct)
    {
        if (!TryParsePlatform(platformRaw, out SocialPlatform platform))
            return Results.BadRequest("Unrecognized platform.");

        SocialVerifyResultResponse result =
            await mediator.Send(new VerifySocialBioCodeCommand(subjectType, id, platform), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Disconnect(
        SocialLinkSubjectType subjectType, Guid id, string platformRaw, ISender mediator, CancellationToken ct)
    {
        if (!TryParsePlatform(platformRaw, out SocialPlatform platform))
            return Results.BadRequest("Unrecognized platform.");

        await mediator.Send(new DisconnectSocialAccountCommand(subjectType, id, platform), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleCallback(
        string platform,
        string? code,
        string? state,
        string? error,
        ISender mediator,
        ISocialOAuthStateSigner stateSigner,
        IAppSettings appSettings,
        CancellationToken ct)
    {
        if (error is not null || code is null || state is null)
            return Results.Redirect($"{appSettings.BaseUrl}/artists?social=denied&platform={platform}");

        if (!stateSigner.TryValidate(state, out SocialLinkSubjectType subjectType, out Guid subjectId, out SocialPlatform signedPlatform))
            return Results.BadRequest("Invalid state parameter.");

        string basePath = subjectType == SocialLinkSubjectType.Studio ? "studios/me" : $"artists/{subjectId}";

        try
        {
            await mediator.Send(
                new ExchangeSocialOAuthCodeCommand(subjectType, subjectId, signedPlatform, code), ct);
        }
        catch (Exception)
        {
            return Results.Redirect($"{appSettings.BaseUrl}/{basePath}?social=error&platform={signedPlatform}");
        }

        return Results.Redirect($"{appSettings.BaseUrl}/{basePath}?social=connected&platform={signedPlatform}");
    }
}
