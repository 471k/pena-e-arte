using MediatR;
using Pena_e_Arte.Application.Help.Commands;
using Pena_e_Arte.Application.Onboarding.Commands;
using Pena_e_Arte.Application.Onboarding.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class HelpEndpoints
{
    public static void MapHelpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/help/search-log", LogHelpSearch)
            .RequireAuthorization("ClientAndAbove");

        app.MapGet("/api/v1/onboarding/tour-status", GetOnboardingTourStatus)
            .RequireAuthorization("ClientAndAbove");
        app.MapPost("/api/v1/onboarding/tour-complete", MarkOnboardingTourComplete)
            .RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> LogHelpSearch(
        LogHelpSearchRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new LogHelpSearchCommand(request), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetOnboardingTourStatus(
        string role,
        ISender mediator,
        CancellationToken ct)
    {
        OnboardingTourStatusResponse result =
            await mediator.Send(new GetOnboardingTourStatusQuery(role), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkOnboardingTourComplete(
        MarkOnboardingTourCompleteRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new MarkOnboardingTourCompleteCommand(request), ct);
        return Results.NoContent();
    }
}
