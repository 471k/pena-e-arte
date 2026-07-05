using MediatR;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Application.DepositRules.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class DepositRuleEndpoints
{
    public static void MapDepositRuleEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/deposit-rules")
            .RequireAuthorization();

        // ClientAndAbove: clients need to read their studio's deposit rules to see
        // deposit amounts while booking. The response carries no owner-sensitive
        // data (name, amount, active flag) and is already tenant-scoped.
        group.MapGet("/",            GetDepositRules).RequireAuthorization("ClientAndAbove");
        group.MapGet("{id:guid}",    GetDepositRule).RequireAuthorization("ClientAndAbove");
        group.MapPost("/",           CreateDepositRule).RequireAuthorization("OwnerOnly");
        group.MapPut("{id:guid}",    UpdateDepositRule).RequireAuthorization("OwnerOnly");
        group.MapDelete("{id:guid}", DeleteDepositRule).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetDepositRules(
        ISender           mediator,
        CancellationToken ct)
    {
        List<DepositRuleResponse> result = await mediator.Send(new GetDepositRulesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetDepositRule(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        DepositRuleResponse result = await mediator.Send(new GetDepositRuleQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateDepositRule(
        CreateDepositRuleRequest request,
        ISender                  mediator,
        CancellationToken        ct)
    {
        DepositRuleResponse result = await mediator.Send(new CreateDepositRuleCommand(request), ct);
        return Results.Created($"/api/v1/deposit-rules/{result.Id}", result);
    }

    private static async Task<IResult> UpdateDepositRule(
        Guid                     id,
        UpdateDepositRuleRequest request,
        ISender                  mediator,
        CancellationToken        ct)
    {
        DepositRuleResponse result = await mediator.Send(new UpdateDepositRuleCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteDepositRule(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteDepositRuleCommand(id), ct);
        return Results.NoContent();
    }
}
