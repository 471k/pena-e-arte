using MediatR;
using Pena_e_Arte.Application.Notifications.Commands;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/notifications")
            .RequireAuthorization();

        group.MapGet("/", GetNotifications).RequireAuthorization("ClientAndAbove");
        group.MapGet("/preferences", GetPreferences).RequireAuthorization("OwnerOnly");
        group.MapPut("/preferences", UpdatePreferences).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetNotifications(
        Guid? recipientId,
        string? channel,
        DateTime? from,
        DateTime? to,
        ISender mediator,
        CancellationToken ct)
    {
        List<NotificationLogResponse> result = await mediator.Send(
            new GetNotificationsQuery(recipientId, channel, from, to), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPreferences(
        ISender mediator,
        CancellationToken ct)
    {
        NotificationPreferencesResponse result =
            await mediator.Send(new GetNotificationPreferencesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdatePreferences(
        UpdateNotificationPreferencesRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new UpdateNotificationPreferencesCommand(request), ct);
        return Results.NoContent();
    }
}
