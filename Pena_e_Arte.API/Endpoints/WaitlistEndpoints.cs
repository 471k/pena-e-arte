using MediatR;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class WaitlistEndpoints
{
    public static void MapWaitlistEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/waitlist");

        // Guest (no account yet) or signed-in client — AllowAnonymous because a valid JWT still
        // populates ICurrentUser even without RequireAuthorization; JoinWaitlistHandler resolves
        // either path itself. Same rate-limit posture as guest booking submission.
        group.MapPost("/", JoinWaitlist).AllowAnonymous().RequireRateLimiting("public-booking");

        group.MapGet("/mine", GetMyWaitlistEntries)
            .RequireAuthorization("ClientAndAbove");
        group.MapGet("/", GetWaitlist)
            .RequireAuthorization("ArtistAndAbove");
        group.MapPost("{id:guid}/mark-booked", MarkWaitlistEntryBooked)
            .RequireAuthorization("ClientAndAbove");
        group.MapDelete("{id:guid}", CancelWaitlistEntry)
            .RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> JoinWaitlist(
        JoinWaitlistRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        WaitlistEntryResponse result = await mediator.Send(new JoinWaitlistCommand(request), ct);
        return Results.Created($"/api/v1/waitlist/{result.Id}", result);
    }

    private static async Task<IResult> GetMyWaitlistEntries(ISender mediator, CancellationToken ct)
    {
        List<WaitlistEntryResponse> result = await mediator.Send(new GetMyWaitlistEntriesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetWaitlist(
        Guid? artistId,
        ISender mediator,
        CancellationToken ct)
    {
        List<WaitlistEntryResponse> result = await mediator.Send(new GetWaitlistQuery(artistId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkWaitlistEntryBooked(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new MarkWaitlistEntryBookedCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelWaitlistEntry(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelWaitlistEntryCommand(id), ct);
        return Results.NoContent();
    }
}
