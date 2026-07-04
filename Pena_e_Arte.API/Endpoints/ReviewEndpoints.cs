using MediatR;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/reviews");

        group.MapPost("{reviewId:guid}/respond", RespondToReview)
             .RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> RespondToReview(
        Guid                   reviewId,
        RespondToReviewRequest request,
        ISender                mediator,
        CancellationToken      ct)
    {
        await mediator.Send(new RespondToReviewCommand(reviewId, request.Response), ct);
        return Results.NoContent();
    }
}
