using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Campaigns;

/// <summary>
/// Resolves the recipient Client list for a Campaign. IgnoreQueryFilters() + explicit
/// StudioId throughout — this is called both from the owner-scoped SendCampaignHandler
/// (ambient tenant present) and from SendCampaignJob (Hangfire job, no ambient tenant at
/// all, same class of exception as GuestPendingUploadCleanupJob/TrafficRollupJob).
/// Client.MarketingOptIn == true is a hard filter applied to every audience mode, including
/// Custom — a custom list is a starting set to narrow, never a way to bypass consent.
/// </summary>
public static class CampaignAudienceExtensions
{
    public static async Task<List<Client>> ResolveCampaignAudienceAsync(
        this IAppDbContext db, Campaign campaign, CancellationToken ct)
    {
        IQueryable<Client> query = db.Clients.IgnoreQueryFilters()
            .Where(c => c.StudioId == campaign.StudioId && c.DeletedAt == null && c.MarketingOptIn);

        switch (campaign.Audience)
        {
            case CampaignAudience.AllClients:
                break;

            case CampaignAudience.ClientsWithNoRecentVisit:
                int days = campaign.NoRecentVisitDays ?? 90;
                DateTime cutoff = DateTime.UtcNow.AddDays(-days);
                query = query.Where(c => !db.Appointments.IgnoreQueryFilters().Any(a =>
                    a.StudioId == campaign.StudioId &&
                    a.DeletedAt == null &&
                    a.ClientId == c.Id &&
                    a.Status == AppointmentStatus.Completed &&
                    a.Date >= cutoff));
                break;

            case CampaignAudience.Custom:
                HashSet<Guid> customIds = campaign.CustomClientIds.ToHashSet();
                query = query.Where(c => customIds.Contains(c.Id));
                break;
        }

        return await query.ToListAsync(ct);
    }
}
