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
        group.MapDelete("{id:guid}", CancelWaitlistEntry)
            .RequireAuthorization("ClientAndAbove");

        // C09 claim page read. AllowAnonymous: a guest on the waitlist has no account, so the claim token in the
        // email is the authorisation. Row in architecture.md's AllowAnonymous Exceptions table. Mitigations: a
        // purpose-bound Data Protection token (not the entry id), the 24h window read from the row, one identical
        // 404 for every invalid token, no personal data in the response, side-effect free, rate limited. POST
        // with the token in the body so it never appears in a URL the server sees.
        app.MapPost("/api/v1/public/waitlist/claim", GetWaitlistClaim)
            .AllowAnonymous().RequireRateLimiting("public-read");
    }

    private static async Task<IResult> GetWaitlistClaim(
        WaitlistClaimRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        WaitlistClaimResponse result = await mediator.Send(new GetWaitlistClaimQuery(request.Token), ct);
        return Results.Ok(result);
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

    private static async Task<IResult> CancelWaitlistEntry(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelWaitlistEntryCommand(id), ct);
        return Results.NoContent();
    }
}
