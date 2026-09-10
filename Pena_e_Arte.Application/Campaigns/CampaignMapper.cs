using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Campaigns;

internal static class CampaignMapper
{
    public static CampaignResponse Map(Campaign c) => new(
        c.Id,
        c.Subject,
        c.BodyHtml,
        c.Audience.ToString(),
        c.Status.ToString(),
        c.NoRecentVisitDays,
        c.CustomClientIds,
        c.SentAt,
        c.RecipientCount,
        c.DeliveredCount,
        c.CreatedAt);
}
