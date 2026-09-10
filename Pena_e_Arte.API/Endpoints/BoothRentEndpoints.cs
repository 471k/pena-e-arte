using MediatR;
using Pena_e_Arte.Application.BoothRent.Commands;
using Pena_e_Arte.Application.BoothRent.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class BoothRentEndpoints
{
    public static void MapBoothRentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/booth-rent")
            .RequireAuthorization();

        group.MapGet("/schedules", GetSchedules).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/schedules", CreateSchedule).RequireAuthorization("OwnerOnly");
        group.MapPut("/schedules/{id:guid}", UpdateSchedule).RequireAuthorization("OwnerOnly");

        group.MapGet("/charges", GetCharges).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/charges/{id:guid}/settle", MarkChargeSettled).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetSchedules(Guid? artistId, ISender mediator, CancellationToken ct)
    {
        List<BoothRentScheduleResponse> result = await mediator.Send(new GetBoothRentSchedulesQuery(artistId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateSchedule(
        CreateBoothRentScheduleRequest request, ISender mediator, CancellationToken ct)
    {
        BoothRentScheduleResponse result = await mediator.Send(new CreateBoothRentScheduleCommand(request), ct);
        return Results.Created($"/api/v1/booth-rent/schedules/{result.Id}", result);
    }

    private static async Task<IResult> UpdateSchedule(
        Guid id, UpdateBoothRentScheduleRequest request, ISender mediator, CancellationToken ct)
    {
        BoothRentScheduleResponse result = await mediator.Send(new UpdateBoothRentScheduleCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCharges(Guid? artistId, ISender mediator, CancellationToken ct)
    {
        List<BoothRentChargeResponse> result = await mediator.Send(new GetBoothRentChargesQuery(artistId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkChargeSettled(
        Guid id, MarkBoothRentChargeSettledRequest request, ISender mediator, CancellationToken ct)
    {
        BoothRentChargeResponse result = await mediator.Send(new MarkBoothRentChargeSettledCommand(id, request), ct);
        return Results.Ok(result);
    }
}
