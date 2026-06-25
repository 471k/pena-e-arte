using System.Security.Claims;
using MediatR;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.API.Endpoints;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/public");

        group.MapGet("/studios/{slug}",          GetPublicStudio).AllowAnonymous();
        group.MapGet("/artists/{slug}",          GetPublicArtist).AllowAnonymous();
        group.MapGet("/studios/nearby",          GetNearbyStudios).AllowAnonymous();
        group.MapGet("/studios/{slug}/reviews",  GetStudioReviews).AllowAnonymous();
        group.MapGet("/artists/{slug}/reviews",  GetArtistReviews).AllowAnonymous();
        group.MapPost("/studios/{slug}/reviews", CreateStudioReview).RequireAuthorization("ClientAndAbove");
        group.MapPost("/artists/{slug}/reviews", CreateArtistReview).RequireAuthorization("ClientAndAbove");
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

    private static async Task<IResult> GetNearbyStudios(
        double            lat,
        double            lng,
        double            radiusKm,
        ISender           mediator,
        CancellationToken ct)
    {
        List<NearbyStudioResponse> result =
            await mediator.Send(new GetNearbyStudiosQuery(lat, lng, radiusKm), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetStudioReviews(
        string            slug,
        ISender           mediator,
        CancellationToken ct)
    {
        List<ReviewResponse> result =
            await mediator.Send(new GetStudioReviewsQuery(slug), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetArtistReviews(
        string            slug,
        ISender           mediator,
        CancellationToken ct)
    {
        List<ReviewResponse> result =
            await mediator.Send(new GetArtistReviewsQuery(slug), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateStudioReview(
        string              slug,
        CreateReviewRequest body,
        ClaimsPrincipal     user,
        ISender             mediator,
        CancellationToken   ct)
    {
        Guid   authorId   = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string authorName = user.FindFirstValue(ClaimTypes.Name)
                         ?? user.FindFirstValue(ClaimTypes.GivenName)
                         ?? "Anonymous";

        await mediator.Send(
            new CreateStudioReviewCommand(slug, authorId, authorName, body.Rating, body.Body), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateArtistReview(
        string              slug,
        CreateReviewRequest body,
        ClaimsPrincipal     user,
        ISender             mediator,
        CancellationToken   ct)
    {
        Guid   authorId   = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string authorName = user.FindFirstValue(ClaimTypes.Name)
                         ?? user.FindFirstValue(ClaimTypes.GivenName)
                         ?? "Anonymous";

        await mediator.Send(
            new CreateArtistReviewCommand(slug, authorId, authorName, body.Rating, body.Body), ct);
        return Results.NoContent();
    }
}

