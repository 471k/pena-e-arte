using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicPlansQuery : IRequest<List<PublicPlanResponse>>;

public class GetPublicPlansHandler(IAppDbContext db)
    : IRequestHandler<GetPublicPlansQuery, List<PublicPlanResponse>>
{
    public async Task<List<PublicPlanResponse>> Handle(GetPublicPlansQuery query, CancellationToken ct)
    {
        // Plan and PlanPrice are platform-wide catalogue entities with no tenant query filter
        // (they are not TenantEntity), so no IgnoreQueryFilters() is needed here — this is
        // the same read GetPlansHandler does, minus everything not meant for an anonymous
        // visitor. A tier with no active price is retired and never shown.
        List<Plan> plans = await db.Plans
            .AsNoTracking()
            .Include(p => p.Prices)
            .Where(p => p.Prices.Any(pp => pp.IsActive))
            .ToListAsync(ct);

        // Ordered in memory (cheapest tier first, name as the tie-break) so the order is
        // deterministic regardless of the database provider's collation.
        return plans
            .Select(ToResponse)
            .OrderBy(p => p.Prices.Min(pp => pp.Price))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static PublicPlanResponse ToResponse(Plan plan)
    {
        List<PlanPrice> activePrices = plan.Prices
            .Where(pp => pp.IsActive)
            .OrderBy(pp => pp.Interval)
            .ToList();

        PlanPrice? monthly = activePrices.FirstOrDefault(pp => pp.Interval == BillingInterval.Monthly);
        PlanPrice? yearly = activePrices.FirstOrDefault(pp => pp.Interval == BillingInterval.Yearly);
        decimal? monthsFree = monthly is not null && yearly is not null
            ? YearlySavingCalculator.MonthsFree(monthly.Price, yearly.Price)
            : null;

        return new PublicPlanResponse(
            plan.Name,
            MrrRules.PlatformCurrency.ToUpperInvariant(),
            activePrices.Select(pp => new PublicPlanPriceResponse(pp.Interval.ToString(), pp.Price)).ToList(),
            monthsFree,
            plan.AllowBrandingRemoval,
            plan.AllowMarketingCampaigns,
            plan.AllowApiAccess,
            plan.MaxArtists,
            plan.MaxAppointmentsPerMonth,
            plan.MaxNotificationsPerMonth,
            plan.MaxStorageGb,
            plan.MaxLocations);
    }
}
