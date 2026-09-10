using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Campaigns.Queries;

/// <summary>Owner's campaign list — draft compose/send history, newest first.</summary>
public record GetCampaignsQuery : IRequest<IReadOnlyList<CampaignResponse>>;

public class GetCampaignsHandler(IAppDbContext db)
    : IRequestHandler<GetCampaignsQuery, IReadOnlyList<CampaignResponse>>
{
    public async Task<IReadOnlyList<CampaignResponse>> Handle(GetCampaignsQuery query, CancellationToken ct) =>
        await db.Campaigns
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CampaignResponse(
                c.Id, c.Subject, c.BodyHtml, c.Audience.ToString(), c.Status.ToString(),
                c.NoRecentVisitDays, c.CustomClientIds, c.SentAt, c.RecipientCount, c.DeliveredCount, c.CreatedAt))
            .ToListAsync(ct);
}
