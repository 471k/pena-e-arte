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

        group.MapGet("/",                              GetArtists).RequireAuthorization("ClientAndAbove");
        group.MapGet("me",                             GetMyArtist).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",                             CreateArtist).RequireAuthorization("OwnerOnly");
        group.MapGet("{id:guid}",                      GetArtist).RequireAuthorization("ClientAndAbove");
        group.MapPut("{id:guid}",                      UpdateArtist).RequireAuthorization("OwnerOnly");
        group.MapPut("{id:guid}/portfolio-images",     UpdatePortfolio).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("{id:guid}",                   DeleteArtist).RequireAuthorization("OwnerOnly");
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
}
