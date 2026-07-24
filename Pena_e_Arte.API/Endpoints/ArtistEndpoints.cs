using MediatR;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Artists.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class ArtistEndpoints
{
    public static void MapArtistEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/artists")
            .RequireAuthorization();

        group.MapGet("/",                                   GetArtists).RequireAuthorization("ClientAndAbove");
        group.MapGet("me",                                  GetMyArtist).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",                                  CreateArtist).RequireAuthorization("OwnerOnly");
        group.MapGet("{id:guid}",                           GetArtist).RequireAuthorization("ClientAndAbove");
        group.MapPut("{id:guid}",                           UpdateArtist).RequireAuthorization("ArtistAndAbove");
        group.MapPut("{id:guid}/portfolio-images",          UpdatePortfolio).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("{id:guid}",                        DeleteArtist).RequireAuthorization("OwnerOnly");
        group.MapPost("{id:guid}/resend-invite",            ResendArtistInvite).RequireAuthorization("OwnerOnly");
        // P-05: Artist Working Hours
        group.MapGet("{id:guid}/schedule",                  GetSchedule).RequireAuthorization("ClientAndAbove");
        group.MapPut("{id:guid}/schedule",                  UpsertSchedule).RequireAuthorization("ArtistAndAbove");
        group.MapPost("{id:guid}/time-off",                 AddTimeOff).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("{id:guid}/time-off/{timeOffId:guid}", DeleteTimeOff).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> GetArtists(
        string?           search,
        ISender           mediator,
        CancellationToken ct)
    {
        List<ArtistResponse> result = await mediator.Send(new GetArtistsQuery(search), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyArtist(
        ISender           mediator,
        CancellationToken ct)
    {
        ArtistResponse result = await mediator.Send(new GetMyArtistQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateArtist(
        CreateArtistRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        ArtistResponse result = await mediator.Send(new CreateArtistCommand(request), ct);
        return Results.Created($"/api/v1/artists/{result.Id}", result);
    }

    private static async Task<IResult> GetArtist(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        ArtistResponse result = await mediator.Send(new GetArtistQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateArtist(
        Guid                id,
        UpdateArtistRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        ArtistResponse result = await mediator.Send(new UpdateArtistCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdatePortfolio(
        Guid                          id,
        UpdateArtistPortfolioRequest  request,
        ISender                       mediator,
        CancellationToken             ct)
    {
        ArtistResponse result = await mediator.Send(new UpdateArtistPortfolioCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteArtist(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteArtistCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResendArtistInvite(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ResendArtistInviteCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSchedule(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        ArtistAvailabilityResponse result = await mediator.Send(new GetArtistScheduleQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpsertSchedule(
        Guid                        id,
        UpsertArtistScheduleRequest body,
        ISender                     mediator,
        CancellationToken           ct)
    {
        IReadOnlyList<ScheduleEntryDto> entries = body.Entries
            .Select(e => new ScheduleEntryDto(e.DayOfWeek, e.StartTime, e.EndTime, e.IsAvailable))
            .ToList();
        await mediator.Send(new UpsertArtistScheduleCommand(id, entries), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddTimeOff(
        Guid                    id,
        AddArtistTimeOffRequest body,
        ISender                 mediator,
        CancellationToken       ct)
    {
        Guid timeOffId = await mediator.Send(
            new AddArtistTimeOffCommand(id, body.StartDate, body.EndDate, body.Reason), ct);
        return Results.Created($"/api/v1/artists/{id}/time-off/{timeOffId}", new { id = timeOffId });
    }

    private static async Task<IResult> DeleteTimeOff(
        Guid              id,
        Guid              timeOffId,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteArtistTimeOffCommand(id, timeOffId), ct);
        return Results.NoContent();
    }
}
