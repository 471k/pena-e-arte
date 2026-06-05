using MediatR;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class IssuerEndpoints
{
    public static void MapIssuerEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder plans = app.MapGroup("/api/v1/plans")
            .RequireAuthorization("IssuerOnly");

        plans.MapGet("/",         GetPlans);
        plans.MapPost("/",        CreatePlan);
        plans.MapPut("{id:guid}", UpdatePlan);
        plans.MapDelete("{id:guid}", DeletePlan);
    }

    private static async Task<IResult> GetPlans(
        ISender           mediator,
        CancellationToken ct)
    {
        List<PlanResponse> result = await mediator.Send(new GetPlansQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePlan(
        CreatePlanRequest request,
        ISender           mediator,
        CancellationToken ct)
    {
        PlanResponse result = await mediator.Send(new CreatePlanCommand(request), ct);
        return Results.Created($"/api/v1/plans/{result.Id}", result);
    }

    private static async Task<IResult> UpdatePlan(
        Guid              id,
        UpdatePlanRequest request,
        ISender           mediator,
        CancellationToken ct)
    {
        PlanResponse result = await mediator.Send(new UpdatePlanCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeletePlan(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeletePlanCommand(id), ct);
        return Results.NoContent();
    }
}
