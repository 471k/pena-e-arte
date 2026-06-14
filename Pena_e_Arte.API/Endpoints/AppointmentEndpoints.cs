using MediatR;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/appointments")
            .RequireAuthorization();

        group.MapGet("/",                       GetAppointments).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/mine",                   GetMyAppointments).RequireAuthorization("ClientAndAbove");
        group.MapGet("{id:guid}",               GetAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",                      CreateAppointment).RequireAuthorization("ClientAndAbove");
        group.MapDelete("{id:guid}",            CancelAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/confirm",     ConfirmAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/complete",    CompleteAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/no-show",     MarkNoShow).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/reschedule",  RescheduleAppointment).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> GetAppointments(
        DateTime?         from,
        DateTime?         to,
        ISender           mediator,
        CancellationToken ct)
    {
        List<AppointmentResponse> result = await mediator.Send(new GetAppointmentsQuery(from, to), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyAppointments(
        ISender           mediator,
        CancellationToken ct)
    {
        List<AppointmentResponse> result = await mediator.Send(new GetMyAppointmentsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAppointment(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new GetAppointmentQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAppointment(
        CreateAppointmentRequest request,
        ISender                  mediator,
        CancellationToken        ct)
    {
        AppointmentResponse result = await mediator.Send(new CreateAppointmentCommand(request), ct);
        return Results.Created($"/api/v1/appointments/{result.Id}", result);
    }

    private static async Task<IResult> CancelAppointment(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelAppointmentCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmAppointment(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new ConfirmAppointmentCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CompleteAppointment(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new CompleteAppointmentCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkNoShow(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new MarkNoShowCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RescheduleAppointment(
        Guid                          id,
        RescheduleAppointmentRequest  request,
        ISender                       mediator,
        CancellationToken             ct)
    {
        AppointmentResponse result = await mediator.Send(new RescheduleAppointmentCommand(id, request), ct);
        return Results.Ok(result);
    }
}
