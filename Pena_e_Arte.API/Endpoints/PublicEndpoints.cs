using System.Security.Claims;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Endpoints;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/public");

        group.MapGet("/studios/{slug}",          GetPublicStudio).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/artists/{slug}",          GetPublicArtist).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/studios/nearby",          GetNearbyStudios).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/studios/{slug}/reviews",  GetStudioReviews).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/artists/{slug}/reviews",  GetArtistReviews).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapPost("/studios/{slug}/reviews", CreateStudioReview)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-write");
        group.MapPost("/artists/{slug}/reviews", CreateArtistReview)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-write");
        group.MapPost("/artists/{slug}/view",    RecordArtistView)
             .AllowAnonymous().RequireRateLimiting("public-write");
        group.MapGet ("/portfolio/feed",                GetPortfolioFeed).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet ("/portfolio/{imageId:guid}/reviews", GetPortfolioImageReviews).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapPost("/portfolio/{imageId:guid}/reviews", CreatePortfolioImageReview)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-write");
        group.MapGet ("/artists/{slug}/instagram-posts", GetArtistInstagramPosts)
             .AllowAnonymous().RequireRateLimiting("public-read");
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
        ClaimsPrincipal   user,
        CancellationToken ct)
    {
        Guid? currentUserId = user.Identity?.IsAuthenticated == true
            ? Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null
            : null;

        PublicArtistResponse? result =
            await mediator.Send(new GetPublicArtistQuery(slug, currentUserId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetNearbyStudios(
        double            lat,
        double            lng,
        double            radiusKm,
        ISender           mediator,
        CancellationToken ct)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180 || radiusKm is <= 0 or > 500)
            return Results.BadRequest("Invalid lat/lng/radiusKm.");

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

    private static async Task<IResult> RecordArtistView(
        string                 slug,
        IAppDbContext          db,
        IConnectionMultiplexer redis,
        CancellationToken      ct)
    {
        // Fire-and-forget view counter. No MediatR needed — no domain invariants.
        // Approved: public, anonymous, write-only to Redis — not business data.
        Guid? artistId = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug && a.DeletedAt == null)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (artistId is null) return Results.NotFound();

        try
        {
            IDatabase redisDb = redis.GetDatabase();
            await redisDb.StringIncrementAsync($"portfolio:views:{artistId}");
        }
        catch
        {
            // Redis unavailable — view count not recorded; non-critical.
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetPortfolioFeed(
        double?           lat,
        double?           lng,
        ISender           mediator,
        CancellationToken ct,
        double            radiusKm = 50,
        int               page     = 1,
        int               pageSize = 24,
        string?           style    = null)
    {
        if (pageSize is < 1 or > 100) pageSize = 24;

        List<PortfolioImageResponse> result = await mediator.Send(
            new GetPortfolioFeedQuery(lat, lng, radiusKm, page, pageSize, style), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPortfolioImageReviews(
        Guid              imageId,
        ISender           mediator,
        CancellationToken ct)
    {
        List<ReviewResponse> result =
            await mediator.Send(new GetPortfolioImageReviewsQuery(imageId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePortfolioImageReview(
        Guid                imageId,
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
            new CreatePortfolioImageReviewCommand(imageId, authorId, authorName, body.Rating, body.Body),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetArtistInstagramPosts(
        string            slug,
        ISender           mediator,
        CancellationToken ct)
    {
        List<InstagramPostResponse> result =
            await mediator.Send(new GetPublicArtistInstagramPostsQuery(slug), ct);
        return Results.Ok(result);
    }
}

