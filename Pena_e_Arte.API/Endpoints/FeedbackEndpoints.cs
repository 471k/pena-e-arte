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
            .RequireAuthorization("ClientAndAbove");

        RouteGroupBuilder mine = app.MapGroup("/api/v1/feedback")
            .RequireAuthorization("ClientAndAbove");

        mine.MapGet("mine", GetMyFeedbackReports);
        mine.MapGet("{id:guid}/messages", GetFeedbackMessages);
        mine.MapPost("{id:guid}/messages", PostFeedbackMessage);

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

    private static async Task<IResult> GetMyFeedbackReports(
        ISender           mediator,
        CancellationToken ct,
        string?           type = null)
    {
        List<FeedbackReportResponse> result = await mediator.Send(new GetMyFeedbackReportsQuery(type), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetFeedbackMessages(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        List<FeedbackMessageResponse> result = await mediator.Send(new GetFeedbackMessagesQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> PostFeedbackMessage(
        Guid                        id,
        PostFeedbackMessageRequest  request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        FeedbackMessageResponse result =
            await mediator.Send(new PostFeedbackMessageCommand(id, request), ct);
        return Results.Created($"/api/v1/feedback/{id}/messages/{result.Id}", result);
    }
}
