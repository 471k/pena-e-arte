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

        group.MapGet("/",            GetAppointments).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",           CreateAppointment).RequireAuthorization("ClientAndAbove");
        group.MapDelete("{id:guid}", CancelAppointment).RequireAuthorization("ArtistAndAbove");
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
}
