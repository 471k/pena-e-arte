using MediatR;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/reports")
            .RequireAuthorization();

        group.MapGet("/revenue-summary", GetRevenueSummary).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetRevenueSummary(
        DateTime?         from,
        DateTime?         to,
        ISender           mediator,
        CancellationToken ct)
    {
        RevenueSummaryResponse result = await mediator.Send(new GetRevenueSummaryQuery(from, to), ct);
        return Results.Ok(result);
    }
}
