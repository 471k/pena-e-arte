using MediatR;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;
using Unit = MediatR.Unit;

namespace Pena_e_Arte.API.Endpoints;

public static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/studios");

        group.MapGet("/map",     GetStudioMap).AllowAnonymous();
        group.MapGet("{id:guid}/qr", GetQrCode).AllowAnonymous();
        group.MapPost("/",       RegisterStudio).AllowAnonymous();
        group.MapPost("/connect", ConnectStudio).RequireAuthorization("OwnerOnly");

        // Owner: view + edit own studio profile
        group.MapGet("/me",  GetMyStudio).RequireAuthorization("OwnerOnly");
        group.MapPut("/me",  UpdateMyStudio).RequireAuthorization("OwnerOnly");

        // Owner: manage branding and slug for their studio
        group.MapPatch("{id:guid}/branding",   UpdateBranding).RequireAuthorization("OwnerOnly");
        group.MapPatch("{id:guid}/slug",       UpdateSlug).RequireAuthorization("OwnerOnly");

        // Issuer: list all studios + suspension controls
        group.MapGet("/",                      GetStudios).RequireAuthorization("IssuerOnly");
        group.MapPatch("{id:guid}/suspend",    SuspendStudio).RequireAuthorization("IssuerOnly");
        group.MapPatch("{id:guid}/unsuspend",  UnsuspendStudio).RequireAuthorization("IssuerOnly");
    }

    private static async Task<IResult> GetStudioMap(
        ISender           mediator,
        CancellationToken ct)
    {
        List<StudioMapItemResponse> result = await mediator.Send(new GetStudioMapQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RegisterStudio(
        RegisterStudioRequest request,
        ISender               mediator,
        CancellationToken     ct)
    {
        StudioResponse result = await mediator.Send(new RegisterStudioCommand(request), ct);
        return Results.Created($"/api/v1/studios/{result.Id}", result);
    }

    private static async Task<IResult> ConnectStudio(
        ConnectStudioRequest  request,
        ISender               mediator,
        CancellationToken     ct)
    {
        ConnectOnboardingResponse result = await mediator.Send(new ConnectStudioCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyStudio(
        ISender           mediator,
        CancellationToken ct)
    {
        StudioResponse result = await mediator.Send(new GetMyStudioQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateMyStudio(
        UpdateStudioRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        StudioResponse result = await mediator.Send(new UpdateMyStudioCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateSlug(
        Guid                      id,
        UpdateStudioSlugRequest   request,
        ISender                   mediator,
        CancellationToken         ct)
    {
        await mediator.Send(new UpdateStudioSlugCommand(id, request.NewSlug), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateBranding(
        Guid                         id,
        UpdateStudioBrandingRequest  request,
        ISender                      mediator,
        CancellationToken            ct)
    {
        StudioResponse result = await mediator.Send(
            new UpdateStudioBrandingCommand(id, request.ShowPlatformBranding), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetStudios(
        ISender           mediator,
        CancellationToken ct)
    {
        List<StudioResponse> result = await mediator.Send(new GetStudiosQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SuspendStudio(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new SuspendStudioCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnsuspendStudio(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new UnsuspendStudioCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetQrCode(
        Guid              id,
        ISender           mediator,
        CancellationToken ct,
        [FromQuery] string? format = "png")
    {
        string fmt = format?.ToLowerInvariant() ?? "png";
        QrCodeResponse result = await mediator.Send(new GetStudioQrCodeQuery(id, fmt), ct);
        return Results.File(result.Data, result.ContentType, $"{result.Slug}-qr.{fmt}");
    }
}
