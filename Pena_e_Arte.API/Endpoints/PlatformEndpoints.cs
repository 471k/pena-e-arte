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
            .RequireAuthorization("AdminOnly");

        group.MapGet("stats", GetStats);
        group.MapGet("studios/{studioId:guid}/summary", GetStudioSummary);
        group.MapGet("mrr-history", GetMrrHistory);
        group.MapGet("subscriptions", GetSubscriptions);
        group.MapPatch("subscriptions/{studioId:guid}/trial", ExtendTrial);
        group.MapPost("studios/{studioId:guid}/subscription/activate", ActivateSubscriptionManually);
        group.MapPatch("subscriptions/{studioId:guid}/cancel", CancelSubscription);
        group.MapGet("referral-codes", GetReferralCodes);
        group.MapPatch("referral-codes/{id:guid}/deactivate", DeactivateReferralCode);
        group.MapPost("studios/{studioId:guid}/referral-codes", GenerateReferralCodeForStudio);
        group.MapPatch("referral-codes/{id:guid}/reactivate", ReactivateReferralCode);
        group.MapDelete("referral-codes/{id:guid}", DeleteReferralCode);
        group.MapGet("reports/industry", GetIndustryReports);
        group.MapPost("reports/industry/trigger", TriggerIndustryReport);
        group.MapGet("plan-usage-report", GetPlanUsageReport);
        group.MapGet("help-search-insights", GetHelpSearchInsights);
        group.MapGet("audit-log", GetAuditLog);
        group.MapGet("traffic/live", GetLiveTrafficSnapshot);
        group.MapGet("traffic/history", GetTrafficHistory);
        group.MapGet("traffic/breakdown", GetTrafficBreakdown);
    }

    private static async Task<IResult> GetStats(
        ISender mediator,
        CancellationToken ct)
    {
        PlatformStatsResponse result = await mediator.Send(new GetPlatformStatsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetStudioSummary(
        Guid studioId,
        ISender mediator,
        CancellationToken ct)
    {
        AdminStudioSummaryResponse result =
            await mediator.Send(new GetAdminStudioSummaryQuery(studioId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMrrHistory(
        ISender mediator,
        int? months,
        CancellationToken ct)
    {
        List<MrrDataPointResponse> result =
            await mediator.Send(new GetMrrHistoryQuery(Math.Clamp(months ?? 12, 1, 24)), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSubscriptions(
        ISender mediator,
        CancellationToken ct)
    {
        List<PlatformSubscriptionResponse> result =
            await mediator.Send(new GetPlatformSubscriptionsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExtendTrial(
        Guid studioId,
        ExtendTrialRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ExtendTrialCommand(studioId, request), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetReferralCodes(
        ISender mediator,
        CancellationToken ct)
    {
        List<PlatformReferralCodeResponse> result =
            await mediator.Send(new GetPlatformReferralCodesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeactivateReferralCode(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeactivateReferralCodeCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ActivateSubscriptionManually(
        Guid studioId,
        ActivateSubscriptionManuallyRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        SubscriptionResponse result = await mediator.Send(
            new ActivateSubscriptionManuallyCommand(studioId, request.PlanId, request.Note), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CancelSubscription(
        Guid studioId,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelSubscriptionCommand(studioId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GenerateReferralCodeForStudio(
        Guid studioId,
        GenerateReferralCodeForStudioRequest? request,
        ISender mediator,
        CancellationToken ct)
    {
        PlatformReferralCodeResponse result =
            await mediator.Send(new AdminGenerateReferralCodeCommand(studioId, request?.ExpiresAt), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ReactivateReferralCode(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ReactivateReferralCodeCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteReferralCode(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteReferralCodeCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetIndustryReports(
        ISender mediator,
        CancellationToken ct)
    {
        IReadOnlyList<IndustryReportSummaryResponse> result =
            await mediator.Send(new GetIndustryReportsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> TriggerIndustryReport(
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new TriggerIndustryReportCommand(), ct);
        return Results.Accepted();
    }

    private static async Task<IResult> GetPlanUsageReport(
        ISender mediator,
        CancellationToken ct)
    {
        PlanUsageReportResponse result = await mediator.Send(new GetPlanUsageReportQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetHelpSearchInsights(
        ISender mediator,
        CancellationToken ct,
        int? days = null)
    {
        HelpSearchInsightsResponse result =
            await mediator.Send(new GetHelpSearchInsightsQuery(Math.Clamp(days ?? 30, 1, 365)), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAuditLog(
        ISender mediator,
        CancellationToken ct,
        string? action = null,
        string? targetType = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20)
    {
        AuditLogPageResponse result = await mediator.Send(
            new GetAuditLogQuery(action, targetType, from, to, page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetLiveTrafficSnapshot(
        ISender mediator,
        CancellationToken ct)
    {
        LiveTrafficSnapshotResponse result = await mediator.Send(new GetLiveTrafficSnapshotQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTrafficHistory(
        ISender mediator,
        CancellationToken ct,
        int? days = null)
    {
        // Range clamping is the handler's job (GetTrafficHistoryQuery.cs) — the sole source of
        // truth, so the bound only needs to change in one place.
        TrafficHistoryResponse result = await mediator.Send(new GetTrafficHistoryQuery(days ?? 30), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTrafficBreakdown(
        ISender mediator,
        CancellationToken ct,
        int? days = null)
    {
        // Range clamping is the handler's job (GetTrafficBreakdownQuery.cs) — the sole source of
        // truth, so the bound only needs to change in one place.
        TrafficBreakdownResponse result = await mediator.Send(new GetTrafficBreakdownQuery(days ?? 30), ct);
        return Results.Ok(result);
    }
}
