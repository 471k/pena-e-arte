using MediatR;
using Pena_e_Arte.Application.ConductReports.Commands;
using Pena_e_Arte.Application.ConductReports.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.API.Endpoints;

public static class ConductReportEndpoints
{
    public static void MapConductReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/studios/me/conduct-reports", GetMyStudioConductReports)
           .RequireAuthorization("OwnerOnly");

        app.MapGet("/api/v1/artists/me/conduct-reports", GetMyConductReportsAsArtist)
           .RequireAuthorization("ArtistAndAbove");

        // Gated OwnerOnly at the route level (which includes issuer); the finer-grained
        // severity split (owner may resolve Standard only, issuer may resolve any) is
        // enforced inside the handler via ConductReportAuthorizationGuard.EnsureCanChangeStatus
        // — matches how RespondToReview is OwnerOnly at the route while finer-grained rules
        // live in the handler layer throughout this codebase.
        app.MapPatch("/api/v1/conduct-reports/{id:guid}/status", UpdateConductReportStatus)
           .RequireAuthorization("OwnerOnly");

        RouteGroupBuilder platform = app.MapGroup("/api/v1/platform/conduct-reports")
            .RequireAuthorization("IssuerOnly");
        platform.MapGet("", GetConductReports);
    }

    private static async Task<IResult> GetMyStudioConductReports(
        ISender mediator, CancellationToken ct, string? status = null)
    {
        List<ConductReportResponse> result =
            await mediator.Send(new GetMyStudioConductReportsQuery(status), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyConductReportsAsArtist(ISender mediator, CancellationToken ct)
    {
        List<ConductReportResponse> result =
            await mediator.Send(new GetMyConductReportsAsArtistQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateConductReportStatus(
        Guid id, UpdateConductReportStatusRequest request, ISender mediator, CancellationToken ct)
    {
        ReportStatus status = Enum.Parse<ReportStatus>(request.Status, ignoreCase: true);
        await mediator.Send(
            new UpdateConductReportStatusCommand(id, status, request.ResolutionNote), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetConductReports(
        ISender mediator, CancellationToken ct,
        string? category = null, string? status = null, Guid? studioId = null)
    {
        List<ConductReportResponse> result =
            await mediator.Send(new GetConductReportsQuery(category, status, studioId), ct);
        return Results.Ok(result);
    }
}
