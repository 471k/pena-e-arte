using MediatR;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Application.Reminders.Queries;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class ManualReminderEndpoints
{
    public static void MapManualReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reminders").RequireAuthorization();

        group.MapPost("/", CreateManualReminder).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/", GetManualReminders).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("/{id:guid}", CancelManualReminder).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> CreateManualReminder(
        CreateManualReminderRequest request, ISender mediator, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateManualReminderCommand(request), ct);
        return Results.Created($"/api/v1/reminders/{result.Id}", result);
    }

    private static async Task<IResult> GetManualReminders(
        Guid? appointmentId, Guid? clientId, ISender mediator, CancellationToken ct)
    {
        var result = await mediator.Send(new GetManualRemindersQuery(appointmentId, clientId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CancelManualReminder(
        Guid id, ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new CancelManualReminderCommand(id), ct);
        return Results.NoContent();
    }
}
