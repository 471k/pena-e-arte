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

        group.MapGet("/connect-url", GetConnectUrl).RequireAuthorization("OwnerOnly");
        group.MapGet("/status", GetStatus).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/posts", GetPosts).RequireAuthorization("ArtistAndAbove");
        group.MapPut("/posts/{postId:guid}/visibility", ToggleVisibility)
             .RequireAuthorization("ArtistAndAbove");
        group.MapDelete("/disconnect", Disconnect).RequireAuthorization("OwnerOnly");
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

    private static async Task<IResult> HandleCallback(
        string? code,
        string? state,
        string? error,
        ISender mediator,
        IInstagramStateSigner stateSigner,
        IAppSettings appSettings,
        CancellationToken ct)
    {
        if (error is not null || code is null || state is null)
            return Results.Redirect($"{appSettings.BaseUrl}/artists?instagram=denied");

        if (!stateSigner.TryValidate(state, out Guid artistId))
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
