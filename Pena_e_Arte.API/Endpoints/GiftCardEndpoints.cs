using MediatR;
using Pena_e_Arte.Application.GiftCards.Commands;
using Pena_e_Arte.Application.GiftCards.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class GiftCardEndpoints
{
    public static void MapGiftCardEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/gift-cards");

        // Guest purchase — same posture as guest checkout.
        group.MapPost("/", PurchaseGiftCard).AllowAnonymous().RequireRateLimiting("public-booking");

        // Enumeration-risk public lookup — see architecture.md AllowAnonymous Exceptions table.
        group.MapGet("{code}/balance", GetGiftCardBalance).AllowAnonymous().RequireRateLimiting("public-read");

        group.MapPost("/redeem", RedeemGiftCard).RequireAuthorization("ClientAndAbove");

        group.MapGet("/", GetGiftCards).RequireAuthorization("OwnerOnly");
        group.MapPost("{id:guid}/void", VoidGiftCard).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> PurchaseGiftCard(
        PurchaseGiftCardRequest request, ISender mediator, CancellationToken ct)
    {
        PurchaseGiftCardResponse result = await mediator.Send(new PurchaseGiftCardCommand(request), ct);
        return Results.Created($"/api/v1/gift-cards/{result.GiftCardId}", result);
    }

    private static async Task<IResult> GetGiftCardBalance(string code, ISender mediator, CancellationToken ct)
    {
        GiftCardBalanceResponse result = await mediator.Send(new GetGiftCardBalanceQuery(code), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RedeemGiftCard(
        RedeemGiftCardRequest request, ISender mediator, CancellationToken ct)
    {
        GiftCardResponse result = await mediator.Send(new RedeemGiftCardCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetGiftCards(ISender mediator, CancellationToken ct)
    {
        List<GiftCardResponse> result = await mediator.Send(new GetGiftCardsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> VoidGiftCard(Guid id, ISender mediator, CancellationToken ct)
    {
        GiftCardResponse result = await mediator.Send(new VoidGiftCardCommand(id), ct);
        return Results.Ok(result);
    }
}
