using MediatR;
using Pena_e_Arte.Application.ClientReferrals.Commands;
using Pena_e_Arte.Application.ClientReferrals.Queries;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class ClientReferralEndpoints
{
    public static void MapClientReferralEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/clients/me/referrals");

        group.MapPost("code", GetOrCreateMyReferralCode).RequireAuthorization("ClientAndAbove");
        group.MapGet("rewards", GetMyReferralRewards).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> GetOrCreateMyReferralCode(ISender mediator, CancellationToken ct)
    {
        ClientReferralCodeResponse result = await mediator.Send(new GetOrCreateMyReferralCodeCommand(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyReferralRewards(ISender mediator, CancellationToken ct)
    {
        IReadOnlyList<ClientReferralRewardResponse> result = await mediator.Send(new GetMyReferralRewardsQuery(), ct);
        return Results.Ok(result);
    }
}
