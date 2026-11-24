using MediatR;
using Pena_e_Arte.Application.BookingDrafts.Commands;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

/// <summary>
/// Saved booking drafts (S04): the signed-in client's own draft at the current studio. ClientOnly, because a
/// draft is a client's unfinished request. Staff book on a client's behalf through their own forms. Tenant
/// isolation comes from the global query filter, and authorship from the handler's UserId check.
/// </summary>
public static class BookingDraftEndpoints
{
    public static void MapBookingDraftEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/booking-drafts").RequireAuthorization("ClientOnly");

        group.MapGet("/mine", GetMine);
        group.MapPut("/mine", SaveMine);
        group.MapDelete("/mine", DeleteMine);
    }

    private static async Task<IResult> GetMine(ISender mediator, CancellationToken ct)
    {
        BookingDraftResponse? draft = await mediator.Send(new GetMyBookingDraftQuery(), ct);
        return draft is null ? Results.NoContent() : Results.Ok(draft);
    }

    private static async Task<IResult> SaveMine(SaveBookingDraftRequest request, ISender mediator, CancellationToken ct)
        => Results.Ok(await mediator.Send(new SaveMyBookingDraftCommand(request), ct));

    private static async Task<IResult> DeleteMine(ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new DeleteMyBookingDraftCommand(), ct);
        return Results.NoContent();
    }
}
