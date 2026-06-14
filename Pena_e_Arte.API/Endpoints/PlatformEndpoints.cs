using MediatR;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/platform")
            .RequireAuthorization("IssuerOnly");

        group.MapGet("stats",                            GetStats);
        group.MapGet("subscriptions",                    GetSubscriptions);
        group.MapPatch("subscriptions/{studioId:guid}/trial", ExtendTrial);
        group.MapPost("studios/{studioId:guid}/subscription/activate", ActivateSubscriptionManually);
        group.MapPatch("subscriptions/{studioId:guid}/cancel",         CancelSubscription);
        group.MapGet("referral-codes",                   GetReferralCodes);
        group.MapPatch("referral-codes/{id:guid}/deactivate", DeactivateReferralCode);
        group.MapGet("reports/industry",                 GetIndustryReports);
    }

    private static async Task<IResult> GetStats(
        ISender           mediator,
        CancellationToken ct)
    {
        PlatformStatsResponse result = await mediator.Send(new GetPlatformStatsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSubscriptions(
        ISender           mediator,
        CancellationToken ct)
    {
        List<PlatformSubscriptionResponse> result =
            await mediator.Send(new GetPlatformSubscriptionsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExtendTrial(
        Guid              studioId,
        ExtendTrialRequest request,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ExtendTrialCommand(studioId, request), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetReferralCodes(
        ISender           mediator,
        CancellationToken ct)
    {
        List<PlatformReferralCodeResponse> result =
            await mediator.Send(new GetPlatformReferralCodesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeactivateReferralCode(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeactivateReferralCodeCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ActivateSubscriptionManually(
        Guid                                studioId,
        ActivateSubscriptionManuallyRequest request,
        ISender                             mediator,
        CancellationToken                   ct)
    {
        SubscriptionResponse result = await mediator.Send(
            new ActivateSubscriptionManuallyCommand(studioId, request.PlanId, request.Note), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CancelSubscription(
        Guid              studioId,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelSubscriptionCommand(studioId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetIndustryReports(
        ISender           mediator,
        CancellationToken ct)
    {
        IReadOnlyList<IndustryReportSummaryResponse> result =
            await mediator.Send(new GetIndustryReportsQuery(), ct);
        return Results.Ok(result);
    }
}
