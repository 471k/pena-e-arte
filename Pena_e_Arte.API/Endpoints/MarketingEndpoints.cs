using MediatR;
using Pena_e_Arte.Application.Public.Commands;

namespace Pena_e_Arte.API.Endpoints;

public static class MarketingEndpoints
{
    public static void MapMarketingEndpoints(this IEndpointRouteBuilder app)
    {
        // AllowAnonymous — a clicked email link carries no session. Signed token
        // (HMAC-SHA256, IMarketingOptOutSigner) validated before trusting clientId. See
        // architecture.md's AllowAnonymous Exceptions table.
        app.MapPost("/api/v1/marketing/unsubscribe", Unsubscribe)
            .AllowAnonymous()
            .RequireRateLimiting("public-write");
    }

    private static async Task<IResult> Unsubscribe(string token, ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new WithdrawMarketingOptInCommand(token), ct);
        return Results.Ok();
    }
}
