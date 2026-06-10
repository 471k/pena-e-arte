using MediatR;
using Pena_e_Arte.Application.Referrals.Commands;
using Pena_e_Arte.Application.Referrals.Queries;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class ReferralEndpoints
{
    public static void MapReferralEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/studios");

        group.MapPost("{id:guid}/referral-codes",
            GenerateReferralCode).RequireAuthorization("OwnerOnly");

        group.MapGet("{id:guid}/referral-codes",
            GetReferralCode).RequireAuthorization("OwnerOnly");

        group.MapGet("{id:guid}/referral-stats",
            GetReferralStats).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GenerateReferralCode(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        ReferralCodeResponse result = await mediator.Send(new GenerateReferralCodeCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetReferralCode(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        ReferralCodeResponse? result = await mediator.Send(new GetReferralCodeQuery(id), ct);
        return result is null ? Results.NoContent() : Results.Ok(result);
    }

    private static async Task<IResult> GetReferralStats(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        ReferralStatsResponse result = await mediator.Send(new GetReferralStatsQuery(id), ct);
        return Results.Ok(result);
    }
}
