using MediatR;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.API.Endpoints;

public static class PublicDesignEndpoints
{
    public static void MapPublicDesignEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/public/designs");

        group.MapGet("/share/{token}", GetSharedDesign).AllowAnonymous().RequireRateLimiting("public-read");
    }

    private static async Task<IResult> GetSharedDesign(
        string            token,
        ISender           mediator,
        CancellationToken ct)
    {
        SharedDesignResponse? result = await mediator.Send(new GetSharedDesignQuery(token), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
