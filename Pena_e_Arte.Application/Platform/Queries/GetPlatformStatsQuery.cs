using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetPlatformStatsQuery : IRequest<PlatformStatsResponse>;

public class GetPlatformStatsHandler(IAppDbContext db)
    : IRequestHandler<GetPlatformStatsQuery, PlatformStatsResponse>
{
    public async Task<PlatformStatsResponse> Handle(GetPlatformStatsQuery query, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime lastMonth = monthStart.AddMonths(-1);

        // Status counts, not revenue — a broader read than MrrInputLoader's (it also needs
        // studios that have never had a Subscription row, e.g. legacy/edge-case data), so it
        // stays its own query rather than being folded into the shared loader.
        // IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, AdminOnly. See architecture.md.
        List<Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
                    .ThenInclude(plan => plan!.Prices)
            .ToListAsync(ct);

        // Suspended = manually deactivated by admin (IsActive = false). These studios are still
        // in the DB but invisible on the platform. They are NOT included in any subscription bucket.
        int suspendedStudios = studios.Count(s => !s.IsActive);

        // All subsequent counts operate only on active studios (IsActive = true).
        List<Studio> active = studios.Where(s => s.IsActive).ToList();

        int totalStudios = studios.Count;
        int activeSubscriptions = active.Count(s => s.Subscription?.Status == SubscriptionStatus.Active);
        int trialStudios = active.Count(s =>
            s.Subscription?.Status == SubscriptionStatus.Trialing
            || (s.Subscription is null && s.TrialExpiresAt > now));
        int gracePeriodStudios = active.Count(s => s.Subscription?.Status == SubscriptionStatus.GracePeriod);
        int pastDueStudios = active.Count(s => s.Subscription?.Status == SubscriptionStatus.PastDue);
        int cancelledStudios = active.Count(s => s.Subscription?.Status == SubscriptionStatus.Cancelled);

        List<SubscriptionRevenueInput> inputs = await MrrInputLoader.LoadAsync(db, ct);

        decimal mrr = MrrRules.MrrAt(inputs, now, now);
        decimal lastMonthMrr = MrrRules.MrrAt(inputs, MrrRules.EndOfMonth(lastMonth), now);
        double? mrrGrowthPercent = lastMonthMrr == 0
            ? null
            : Math.Round((double)((mrr - lastMonthMrr) / lastMonthMrr) * 100, 1);

        decimal atRiskMrr = MrrRules.AtRiskMrr(inputs);
        decimal scheduledChurnMrr = MrrRules.ScheduledChurnMrr(inputs);
        decimal pausedMrr = MrrRules.PausedMrr(inputs);
        int payingStudios = MrrRules.PayingStudios(inputs);

        int conversionDenominator = activeSubscriptions + trialStudios + gracePeriodStudios;
        double trialConversionRate = conversionDenominator > 0
            ? Math.Round((double)activeSubscriptions / conversionDenominator, 4)
            : 0;

        int newStudiosThisMonth = studios.Count(s => s.CreatedAt >= monthStart);

        return new PlatformStatsResponse(
            totalStudios,
            activeSubscriptions,
            trialStudios,
            gracePeriodStudios,
            pastDueStudios,
            cancelledStudios,
            suspendedStudios,
            mrr,
            mrrGrowthPercent,
            trialConversionRate,
            newStudiosThisMonth,
            payingStudios,
            atRiskMrr,
            scheduledChurnMrr,
            pausedMrr);
    }
}
