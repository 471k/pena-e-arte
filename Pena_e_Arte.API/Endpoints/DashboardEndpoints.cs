using MediatR;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

/// <summary>The owner's home (E05a). Setup readiness lives with the studio: GET /studios/me/setup-readiness.</summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/dashboard")
            .RequireAuthorization();

        group.MapGet("/exceptions", GetExceptions).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetExceptions(ISender mediator, CancellationToken ct)
    {
        OwnerExceptionsResponse result = await mediator.Send(new GetOwnerExceptionsQuery(), ct);
        return Results.Ok(result);
    }
}
