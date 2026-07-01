using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pena_e_Arte.Application.Saved.Commands;
using Pena_e_Arte.Application.Saved.Queries;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.API.Endpoints;

public static class SavedImagesEndpoints
{
    public static void MapSavedImagesEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/saved-images")
            .RequireAuthorization("ClientAndAbove");

        group.MapGet("/",                    GetSavedImages);
        group.MapGet("/ids",                 GetSavedImageIds);
        group.MapPost("/{imageId:guid}",     SaveImage);
        group.MapDelete("/{imageId:guid}",   UnsaveImage);
    }

    private static async Task<IResult> GetSavedImages(
        ClaimsPrincipal   user,
        ISender           mediator,
        [FromQuery] int   page = 1,
        CancellationToken ct   = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        List<PortfolioImageResponse> result =
            await mediator.Send(new GetSavedPortfolioImagesQuery(userId, page), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSavedImageIds(
        ClaimsPrincipal   user,
        ISender           mediator,
        CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        HashSet<Guid> result = await mediator.Send(new GetSavedImageIdsQuery(userId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SaveImage(
        Guid              imageId,
        ClaimsPrincipal   user,
        ISender           mediator,
        CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new SavePortfolioImageCommand(userId, imageId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnsaveImage(
        Guid              imageId,
        ClaimsPrincipal   user,
        ISender           mediator,
        CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new UnsavePortfolioImageCommand(userId, imageId), ct);
        return Results.NoContent();
    }
}
