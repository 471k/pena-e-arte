using MediatR;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Application.Feedback.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/feedback", SubmitFeedback)
            .RequireAuthorization("ArtistAndAbove");

        RouteGroupBuilder group = app.MapGroup("/api/v1/platform/feedback")
            .RequireAuthorization("IssuerOnly");

        group.MapGet("", GetFeedbackReports);
        group.MapPatch("{id:guid}/status", UpdateFeedbackStatus);
    }

    private static async Task<IResult> SubmitFeedback(
        SubmitFeedbackRequest request,
        ISender               mediator,
        CancellationToken     ct)
    {
        FeedbackReportResponse result = await mediator.Send(new SubmitFeedbackCommand(request), ct);
        return Results.Created($"/api/v1/feedback/{result.Id}", result);
    }

    private static async Task<IResult> GetFeedbackReports(
        ISender           mediator,
        CancellationToken ct,
        string?           type   = null,
        string?           status = null)
    {
        List<FeedbackReportResponse> result =
            await mediator.Send(new GetFeedbackReportsQuery(type, status), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateFeedbackStatus(
        Guid                        id,
        UpdateFeedbackStatusRequest request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        FeedbackReportResponse result =
            await mediator.Send(new UpdateFeedbackStatusCommand(id, request), ct);
        return Results.Ok(result);
    }
}
