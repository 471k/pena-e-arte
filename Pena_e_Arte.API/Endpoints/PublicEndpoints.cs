using MediatR;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.API.Endpoints;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/public");

        group.MapGet("/studios/{slug}", GetPublicStudio).AllowAnonymous();
        group.MapGet("/artists/{slug}", GetPublicArtist).AllowAnonymous();
    }

    private static async Task<IResult> GetPublicStudio(
        string            slug,
        ISender           mediator,
        CancellationToken ct)
    {
        PublicStudioResponse? result = await mediator.Send(new GetPublicStudioQuery(slug), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetPublicArtist(
        string            slug,
        ISender           mediator,
        CancellationToken ct)
    {
        PublicArtistResponse? result = await mediator.Send(new GetPublicArtistQuery(slug), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
