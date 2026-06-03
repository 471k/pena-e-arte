using MediatR;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class StudioEndpoints
{
    public static void MapStudioEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/studios");

        group.MapGet("/map",     GetStudioMap).AllowAnonymous();
        group.MapPost("/",       RegisterStudio).AllowAnonymous();
        group.MapPost("/connect", ConnectStudio).RequireAuthorization("OwnerOnly");
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
}
