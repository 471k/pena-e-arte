using MediatR;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/platform")
            .RequireAuthorization("IssuerOnly");

        group.MapGet("reports/industry", GetIndustryReports);
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
