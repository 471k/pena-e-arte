using MediatR;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Application.Instagram.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.API.Endpoints;

public static class InstagramEndpoints
{
    public static void MapInstagramEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/artists/{id:guid}/instagram")
            .RequireAuthorization();

        // ArtistAndAbove + a handler-side ownership guard (ArtistOwnershipGuard): an artist may
        // connect/disconnect their OWN Instagram — the account holder is the one who can complete
        // the consent screen — while owner/admin keep access to every artist in the studio.
        group.MapGet("/connect-url", GetConnectUrl).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/status", GetStatus).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/posts", GetPosts).RequireAuthorization("ArtistAndAbove");
        group.MapPut("/posts/{postId:guid}/visibility", ToggleVisibility)
             .RequireAuthorization("ArtistAndAbove");
        group.MapDelete("/disconnect", Disconnect).RequireAuthorization("ArtistAndAbove");
    }

    /// <summary>
    /// Public OAuth callback — called by Instagram after the user authorises.
    /// Not authenticated; the signed `state` param is what's trusted, not the caller.
    /// </summary>
    public static void MapInstagramCallbackEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/instagram/callback", HandleCallback)
            .AllowAnonymous()
            .RequireRateLimiting("public-write");

        // Meta's required Deauthorize + Data Deletion callbacks (App Review). Called by Meta's
        // servers, no JWT possible — the signed_request HMAC is what's trusted. See
        // architecture.md's AllowAnonymous Exceptions rows.
        app.MapPost("/api/v1/instagram/deauthorize", Deauthorize)
            .AllowAnonymous()
            .RequireRateLimiting("public-write");
        app.MapPost("/api/v1/instagram/data-deletion", DataDeletion)
            .AllowAnonymous()
            .RequireRateLimiting("public-write");
    }

    private static async Task<IResult> Deauthorize(
        HttpRequest request,
        ISender mediator,
        IMetaSignedRequestParser signedRequestParser,
        CancellationToken ct) =>
        await HandleDeauthorize(await ReadSignedRequestAsync(request, ct), signedRequestParser, mediator, ct);

    private static async Task<IResult> DataDeletion(
        HttpRequest request,
        ISender mediator,
        IMetaSignedRequestParser signedRequestParser,
        IAppSettings appSettings,
        CancellationToken ct) =>
        await HandleDataDeletion(
            await ReadSignedRequestAsync(request, ct), signedRequestParser, mediator, appSettings, ct);

    // Read from the form by hand rather than binding [FromForm]: form-bound minimal-API endpoints
    // demand an antiforgery token, which a server-to-server Meta call can never send.
    private static async Task<string?> ReadSignedRequestAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType) return null;

        IFormCollection form = await request.ReadFormAsync(ct);
        return form["signed_request"].FirstOrDefault();
    }

    // internal (not private) so the verification branches are unit-testable.
    internal static async Task<IResult> HandleDeauthorize(
        string? signedRequest,
        IMetaSignedRequestParser signedRequestParser,
        ISender mediator,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(signedRequest)
            || !signedRequestParser.TryGetUserId(signedRequest, out string instagramUserId))
            return Results.BadRequest("Invalid signed_request.");

        await mediator.Send(new EraseInstagramDataCommand(instagramUserId), ct);
        return Results.Ok();
    }

    internal static async Task<IResult> HandleDataDeletion(
        string? signedRequest,
        IMetaSignedRequestParser signedRequestParser,
        ISender mediator,
        IAppSettings appSettings,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(signedRequest)
            || !signedRequestParser.TryGetUserId(signedRequest, out string instagramUserId))
            return Results.BadRequest("Invalid signed_request.");

        await mediator.Send(new EraseInstagramDataCommand(instagramUserId), ct);

        // Erasure above is synchronous, so the status page can truthfully say "completed". A random
        // code (not derived from the Instagram user id) keeps the id out of the URL Meta shows.
        string confirmationCode = Guid.NewGuid().ToString("N");
        return Results.Ok(new InstagramDataDeletionResponse(
            $"{appSettings.BaseUrl}/data-deletion/instagram?code={confirmationCode}",
            confirmationCode));
    }

    private static async Task<IResult> GetConnectUrl(
        Guid id, ISender mediator, CancellationToken ct)
    {
        string url = await mediator.Send(new GetInstagramConnectUrlQuery(id), ct);
        return Results.Ok(new ConnectInstagramResponse(url));
    }

    private static async Task<IResult> GetStatus(
        Guid id, ISender mediator, CancellationToken ct)
    {
        InstagramConnectionStatusResponse result =
            await mediator.Send(new GetInstagramConnectionStatusQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPosts(
        Guid id, int page, ISender mediator, CancellationToken ct)
    {
        List<InstagramPostResponse> result =
            await mediator.Send(new GetInstagramPostsQuery(id, page == 0 ? 1 : page), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ToggleVisibility(
        Guid id,
        Guid postId,
        TogglePostVisibilityRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ToggleInstagramPostVisibilityCommand(id, postId, request.IsVisible), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Disconnect(
        Guid id, ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new DisconnectInstagramCommand(id), ct);
        return Results.NoContent();
    }

    // internal (not private) so the denial-redirect branches are unit-testable.
    internal static async Task<IResult> HandleCallback(
        string? code,
        string? state,
        string? error,
        ISender mediator,
        IInstagramStateSigner stateSigner,
        IAppSettings appSettings,
        CancellationToken ct)
    {
        if (error is not null || code is null)
        {
            // Instagram echoes `state` back even when the user denies consent, so land the user
            // on their own artist page when it decodes; fall back to the list otherwise.
            if (state is not null && stateSigner.TryValidate(state, out Guid deniedArtistId))
                return Results.Redirect($"{appSettings.BaseUrl}/artists/{deniedArtistId}?instagram=denied");

            return Results.Redirect($"{appSettings.BaseUrl}/artists?instagram=denied");
        }

        if (state is null || !stateSigner.TryValidate(state, out Guid artistId))
            return Results.BadRequest("Invalid state parameter.");

        try
        {
            await mediator.Send(new ExchangeInstagramCodeCommand(artistId, code), ct);
        }
        catch (Exception)
        {
            return Results.Redirect($"{appSettings.BaseUrl}/artists/{artistId}?instagram=error");
        }

        return Results.Redirect($"{appSettings.BaseUrl}/artists/{artistId}?instagram=connected");
    }
}
