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
        group.MapGet("studios/{studioId:guid}/summary",  GetStudioSummary);
        group.MapGet("mrr-history",                      GetMrrHistory);
        group.MapGet("subscriptions",                    GetSubscriptions);
        group.MapPatch("subscriptions/{studioId:guid}/trial", ExtendTrial);
        group.MapPost("studios/{studioId:guid}/subscription/activate", ActivateSubscriptionManually);
        group.MapPatch("subscriptions/{studioId:guid}/cancel",         CancelSubscription);
        group.MapGet("referral-codes",                              GetReferralCodes);
        group.MapPatch("referral-codes/{id:guid}/deactivate",       DeactivateReferralCode);
        group.MapPost("studios/{studioId:guid}/referral-codes",     GenerateReferralCodeForStudio);
        group.MapPatch("referral-codes/{id:guid}/reactivate",       ReactivateReferralCode);
        group.MapDelete("referral-codes/{id:guid}",                 DeleteReferralCode);
        group.MapGet("reports/industry",                            GetIndustryReports);
        group.MapPost("reports/industry/trigger",                   TriggerIndustryReport);
    }

    private static async Task<IResult> GetStats(
        ISender           mediator,
        CancellationToken ct)
    {
        PlatformStatsResponse result = await mediator.Send(new GetPlatformStatsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetStudioSummary(
        Guid              studioId,
        ISender           mediator,
        CancellationToken ct)
    {
        IssuerStudioSummaryResponse result =
            await mediator.Send(new GetIssuerStudioSummaryQuery(studioId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMrrHistory(
        ISender           mediator,
        int?              months,
        CancellationToken ct)
    {
        List<MrrDataPointResponse> result =
            await mediator.Send(new GetMrrHistoryQuery(Math.Clamp(months ?? 12, 1, 24)), ct);
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

    private static async Task<IResult> GenerateReferralCodeForStudio(
        Guid                                 studioId,
        GenerateReferralCodeForStudioRequest? request,
        ISender                              mediator,
        CancellationToken                    ct)
    {
        PlatformReferralCodeResponse result =
            await mediator.Send(new IssuerGenerateReferralCodeCommand(studioId, request?.ExpiresAt), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ReactivateReferralCode(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ReactivateReferralCodeCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteReferralCode(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteReferralCodeCommand(id), ct);
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

    private static async Task<IResult> TriggerIndustryReport(
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new TriggerIndustryReportCommand(), ct);
        return Results.Accepted();
    }
}
