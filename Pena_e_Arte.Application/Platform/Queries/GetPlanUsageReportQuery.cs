using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetPlanUsageReportQuery : IRequest<PlanUsageReportResponse>;

public class GetPlanUsageReportHandler(IAppDbContext db)
    : IRequestHandler<GetPlanUsageReportQuery, PlanUsageReportResponse>
{
    public async Task<PlanUsageReportResponse> Handle(GetPlanUsageReportQuery query, CancellationToken ct)
    {
        DateTime monthStart = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // IgnoreQueryFilters approved: usage #25 — cross-tenant aggregate reads for the issuer
        // plan-usage validation report. See architecture.md.
        List<Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
            .ToListAsync(ct);

        Dictionary<Guid, int> artistCounts = await db.Artists
            .IgnoreQueryFilters()
            .GroupBy(a => a.StudioId)
            .Select(g => new { StudioId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudioId, x => x.Count, ct);

        Dictionary<Guid, int> appointmentCounts = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.CreatedAt >= monthStart)
            .GroupBy(a => a.StudioId)
            .Select(g => new { StudioId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudioId, x => x.Count, ct);

        Dictionary<Guid, int> notificationCounts = await db.NotificationLogs
            .IgnoreQueryFilters()
            .Where(n => n.CreatedAt >= monthStart)
            .GroupBy(n => n.StudioId)
            .Select(g => new { StudioId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudioId, x => x.Count, ct);

        List<StudioPlanUsageRow> rows = studios
            .Where(s => s.Subscription?.Plan is not null)
            .Select(s =>
            {
                Plan plan = s.Subscription!.Plan!;
                double storageGb = Math.Round(s.StorageUsageBytes / 1024.0 / 1024.0 / 1024.0, 1);

                return new StudioPlanUsageRow(
                    s.Id, s.Name, plan.Name,
                    artistCounts.GetValueOrDefault(s.Id), plan.MaxArtists,
                    appointmentCounts.GetValueOrDefault(s.Id), plan.MaxAppointmentsPerMonth,
                    notificationCounts.GetValueOrDefault(s.Id), plan.MaxNotificationsPerMonth,
                    storageGb, plan.MaxStorageGb);
            })
            // Studios closest to any of their caps first, so the issuer can scan
            // top-to-bottom for "who's about to hit a wall" without manually sorting.
            .OrderByDescending(ClosestCapPercent)
            .ToList();

        return new PlanUsageReportResponse(rows);
    }

    private static double ClosestCapPercent(StudioPlanUsageRow r)
    {
        double[] pcts =
        [
            r.MaxArtists              is int ma  && ma  > 0 ? (double)r.ArtistCount / ma   : 0,
            r.MaxAppointmentsPerMonth is int map && map > 0 ? (double)r.AppointmentsThisMonth / map : 0,
            r.MaxNotificationsPerMonth is int mnp && mnp > 0 ? (double)r.NotificationsThisMonth / mnp : 0,
            r.MaxStorageGb            is int msg && msg > 0 ? r.StorageGbUsed / msg : 0,
        ];
        return pcts.Max();
    }
}
