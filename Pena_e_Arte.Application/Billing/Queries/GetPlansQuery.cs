using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Billing.Queries;

public record GetPlansQuery(bool IncludeRetired = false) : IRequest<List<PlanResponse>>;

public class GetPlansHandler(IAppDbContext db, ILogger<GetPlansHandler> logger)
    : IRequestHandler<GetPlansQuery, List<PlanResponse>>
{
    public async Task<List<PlanResponse>> Handle(GetPlansQuery query, CancellationToken ct)
    {
        IQueryable<Plan> plans = db.Plans.Include(p => p.Prices);

        if (!query.IncludeRetired)
            plans = plans.Where(p => p.Prices.Any(pp => pp.IsActive));

        List<PlanResponse> results = await plans
            .OrderBy(p => p.Prices.Min(pp => pp.Price))
            .Select(p => new PlanResponse(
                p.Id,
                p.Name,
                p.YearlyDiscountPercent,
                p.AllowBrandingRemoval,
                db.Subscriptions.Count(s => s.PlanId == p.Id),
                p.MaxArtists,
                p.MaxAppointmentsPerMonth,
                p.MaxNotificationsPerMonth,
                p.MaxStorageGb,
                p.MaxLocations,
                p.AllowApiAccess,
                p.PrioritySupport,
                p.AllowMarketingCampaigns,
                p.Prices.Select(pp => new PlanPriceResponse(
                    pp.Id, pp.Interval.ToString(), pp.Price, pp.StripePriceId, pp.IsActive)).ToList(),
                null,
                null))
            .ToListAsync(ct);

        // D7 — the yearly saving is computed here, from the materialised active
        // Monthly/Yearly prices, rather than in the query above: it needs conditional
        // logic (a warning log for a non-positive saving) that doesn't belong in SQL.
        for (int i = 0; i < results.Count; i++)
            results[i] = WithYearlySaving(results[i]);

        return results;
    }

    private PlanResponse WithYearlySaving(PlanResponse plan)
    {
        PlanPriceResponse? monthly = plan.Prices.FirstOrDefault(p => p.Interval == "Monthly" && p.IsActive);
        PlanPriceResponse? yearly = plan.Prices.FirstOrDefault(p => p.Interval == "Yearly" && p.IsActive);

        if (monthly is null || yearly is null || monthly.Price == 0)
            return plan;

        decimal saving = monthly.Price * 12 - yearly.Price;
        decimal? monthsFree = YearlySavingCalculator.MonthsFree(monthly.Price, yearly.Price);
        if (monthsFree is null)
        {
            logger.LogWarning(
                "Plan {PlanId} yearly price does not save against monthly (computed saving {Saving})",
                plan.Id, saving);
            return plan;
        }

        return plan with { YearlySavingAmount = saving, YearlyMonthsFree = monthsFree };
    }
}
