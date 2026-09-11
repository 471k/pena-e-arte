using MediatR;
using Pena_e_Arte.Application.ExternalApi.Queries;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.ExternalApi;

namespace Pena_e_Arte.API.Endpoints;

/// <summary>
/// The actual external-facing API surface — everything here is authenticated via an
/// X-Api-Key header (see Authentication/ApiKeyAuthenticationHandler.cs), not the normal
/// JWT cookie/header flow, and requires the "ExternalApiAccess" policy specifically.
/// Read-only for this first version: no endpoint here can create, update, or delete
/// anything. See docs/claude/architecture.md's "External API Access" entry.
/// </summary>
public static class ExternalApiEndpoints
{
    public static void MapExternalApiEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/external")
            .RequireAuthorization("ExternalApiAccess")
            .RequireRateLimiting("external-api");

        group.MapGet("/appointments", GetAppointments);
        group.MapGet("/clients", GetClients);
        group.MapGet("/revenue", GetRevenue);
    }

    private static async Task<IResult> GetAppointments(
        ISender mediator, CancellationToken ct, int page = 1, int pageSize = 50)
    {
        List<ExternalAppointmentResponse> result =
            await mediator.Send(new GetExternalAppointmentsQuery(page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetClients(
        ISender mediator, CancellationToken ct, int page = 1, int pageSize = 50)
    {
        List<ExternalClientResponse> result =
            await mediator.Send(new GetExternalClientsQuery(page, pageSize), ct);
        return Results.Ok(result);
    }

    // Reuses the exact same query ReportsPage's revenue chart calls internally — already
    // aggregate-only (monthly totals + per-artist totals), no PII, no pagination needed.
    private static async Task<IResult> GetRevenue(
        ISender mediator, CancellationToken ct, DateTime? from = null, DateTime? to = null)
    {
        RevenueSummaryResponse result = await mediator.Send(new GetRevenueSummaryQuery(from, to), ct);
        return Results.Ok(result);
    }
}
