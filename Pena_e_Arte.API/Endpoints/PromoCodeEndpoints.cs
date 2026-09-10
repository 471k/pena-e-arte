using MediatR;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Application.PromoCodes.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class PromoCodeEndpoints
{
    public static void MapPromoCodeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/promo-codes")
            .RequireAuthorization("OwnerOnly");

        group.MapGet("/", GetPromoCodes);
        group.MapGet("{id:guid}", GetPromoCode);
        group.MapPost("/", CreatePromoCode);
        group.MapPut("{id:guid}", UpdatePromoCode);
        group.MapDelete("{id:guid}", DeletePromoCode);
    }

    private static async Task<IResult> GetPromoCodes(
        ISender mediator,
        CancellationToken ct)
    {
        List<PromoCodeResponse> result = await mediator.Send(new GetPromoCodesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPromoCode(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        PromoCodeResponse result = await mediator.Send(new GetPromoCodeQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePromoCode(
        CreatePromoCodeRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PromoCodeResponse result = await mediator.Send(new CreatePromoCodeCommand(request), ct);
        return Results.Created($"/api/v1/promo-codes/{result.Id}", result);
    }

    private static async Task<IResult> UpdatePromoCode(
        Guid id,
        UpdatePromoCodeRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PromoCodeResponse result = await mediator.Send(new UpdatePromoCodeCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeletePromoCode(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeletePromoCodeCommand(id), ct);
        return Results.NoContent();
    }
}
