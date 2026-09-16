using MediatR;
using Pena_e_Arte.Application.SavedPaymentMethods.Commands;
using Pena_e_Arte.Application.SavedPaymentMethods.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class SavedPaymentMethodEndpoints
{
    public static void MapSavedPaymentMethodEndpoints(this IEndpointRouteBuilder app)
    {
        // ClientAndAbove, never ClientOnly — same "my own X" self-service convention as
        // SavedImagesEndpoints/ClientReferralEndpoints; identity is resolved server-side from
        // the caller's own Client record, never trusted from the route.
        RouteGroupBuilder group = app.MapGroup("/api/v1/saved-payment-methods")
            .RequireAuthorization("ClientAndAbove");

        group.MapGet("/", GetSavedPaymentMethods);
        group.MapPost("/", AddSavedPaymentMethod).RequireRateLimiting("billing");
        group.MapDelete("/{id:guid}", DeleteSavedPaymentMethod);
    }

    private static async Task<IResult> GetSavedPaymentMethods(
        ISender mediator,
        CancellationToken ct)
    {
        List<SavedPaymentMethodResponse> result = await mediator.Send(new GetSavedPaymentMethodsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> AddSavedPaymentMethod(
        AddSavedPaymentMethodRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        SavedPaymentMethodResponse result = await mediator.Send(new AddSavedPaymentMethodCommand(request), ct);
        return Results.Created($"/api/v1/saved-payment-methods/{result.Id}", result);
    }

    private static async Task<IResult> DeleteSavedPaymentMethod(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteSavedPaymentMethodCommand(id), ct);
        return Results.NoContent();
    }
}
