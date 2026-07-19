using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
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
        DateTime now        = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime lastMonth  = monthStart.AddMonths(-1);

        // IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, IssuerOnly. See architecture.md.
        List<Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
                    .ThenInclude(plan => plan!.Prices)
            .ToListAsync(ct);

        // Suspended = manually deactivated by issuer (IsActive = false). These studios are still
        // in the DB but invisible on the platform. They are NOT included in any subscription bucket.
        int suspendedStudios = studios.Count(s => !s.IsActive);

        // All subsequent counts operate only on active studios (IsActive = true).
        List<Studio> active = studios.Where(s => s.IsActive).ToList();

        int totalStudios        = studios.Count;
        int activeSubscriptions = active.Count(s => s.Subscription?.Status == SubscriptionStatus.Active);
        int trialStudios        = active.Count(s =>
            s.Subscription?.Status == SubscriptionStatus.Trialing
            || (s.Subscription is null && s.TrialExpiresAt > now));
        int gracePeriodStudios  = active.Count(s => s.Subscription?.Status == SubscriptionStatus.GracePeriod);
        int pastDueStudios      = active.Count(s => s.Subscription?.Status == SubscriptionStatus.PastDue);
        int cancelledStudios    = active.Count(s => s.Subscription?.Status == SubscriptionStatus.Cancelled);

        // MRR — active subscriptions only, sum of each subscription's monthly-equivalent price
        // (a Yearly-billed subscription contributes Price / 12, not the Plan's decorative
        // Monthly reference figure — see architecture.md Decisions Log, "Plan/PlanPrice split").
        decimal mrr = active
            .Where(s => s.Subscription?.Status == SubscriptionStatus.Active && s.Subscription.Plan is not null)
            .Sum(s => MonthlyEquivalentRevenue(s.Subscription!));

        // MRR growth: compare with last calendar month.
        // Approximation: counts active subs that existed before this month and whose period covered last month.
        // Undercounts if any subs were active last month but since cancelled — acceptable at current scale.
        decimal lastMonthMrr = active
            .Where(s =>
                s.Subscription is not null
                && s.Subscription.Plan is not null
                && s.Subscription.CreatedAt < monthStart       // existed last month
                && s.Subscription.CurrentPeriodEnd >= lastMonth // was active last month
                && s.Subscription.Status == SubscriptionStatus.Active)
            .Sum(s => MonthlyEquivalentRevenue(s.Subscription!));

        double mrrGrowthPercent = lastMonthMrr == 0
            ? (mrr > 0 ? 100.0 : 0.0)
            : Math.Round((double)((mrr - lastMonthMrr) / lastMonthMrr) * 100, 1);

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
            newStudiosThisMonth);
    }

    private static decimal MonthlyEquivalentRevenue(Subscription s) =>
        s.Plan?.Prices.FirstOrDefault(pp => pp.Interval == s.BillingInterval) is PlanPrice pp
            ? (pp.Interval == BillingInterval.Monthly ? pp.Price : pp.Price / 12m)
            : 0m;
}
