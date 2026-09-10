using MediatR;
using Pena_e_Arte.Application.Campaigns.Commands;
using Pena_e_Arte.Application.Campaigns.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/campaigns");

        group.MapGet("", GetCampaigns).RequireAuthorization("OwnerOnly");
        group.MapPost("", CreateCampaign).RequireAuthorization("OwnerOnly");
        group.MapPost("{id:guid}/send", SendCampaign).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetCampaigns(ISender mediator, CancellationToken ct)
    {
        IReadOnlyList<CampaignResponse> result = await mediator.Send(new GetCampaignsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateCampaign(
        CreateCampaignRequest request, ISender mediator, CancellationToken ct)
    {
        CampaignResponse result = await mediator.Send(new CreateCampaignCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SendCampaign(Guid id, ISender mediator, CancellationToken ct)
    {
        CampaignResponse result = await mediator.Send(new SendCampaignCommand(id), ct);
        return Results.Ok(result);
    }
}
