using MediatR;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Application.Services.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class ServiceEndpoints
{
    public static void MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/services")
            .RequireAuthorization();

        // ClientAndAbove: clients need to read the studio's service catalog to pick one
        // while booking. The response carries no owner-sensitive data and is already
        // tenant-scoped.
        group.MapGet("/", GetServices).RequireAuthorization("ClientAndAbove");
        group.MapGet("{id:guid}", GetService).RequireAuthorization("ClientAndAbove");
        group.MapPost("/", CreateService).RequireAuthorization("OwnerOnly");
        group.MapPut("{id:guid}", UpdateService).RequireAuthorization("OwnerOnly");
        group.MapDelete("{id:guid}", DeleteService).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetServices(
        ISender mediator,
        CancellationToken ct)
    {
        List<ServiceResponse> result = await mediator.Send(new GetServicesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetService(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        ServiceResponse result = await mediator.Send(new GetServiceQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateService(
        CreateServiceRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        ServiceResponse result = await mediator.Send(new CreateServiceCommand(request), ct);
        return Results.Created($"/api/v1/services/{result.Id}", result);
    }

    private static async Task<IResult> UpdateService(
        Guid id,
        UpdateServiceRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        ServiceResponse result = await mediator.Send(new UpdateServiceCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteService(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteServiceCommand(id), ct);
        return Results.NoContent();
    }
}
