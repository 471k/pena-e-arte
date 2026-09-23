using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Billing.Queries;

public record GetPlansQuery(bool IncludeRetired = false) : IRequest<List<PlanResponse>>;

public class GetPlansHandler(IAppDbContext db)
    : IRequestHandler<GetPlansQuery, List<PlanResponse>>
{
    public async Task<List<PlanResponse>> Handle(GetPlansQuery query, CancellationToken ct)
    {
        IQueryable<Plan> plans = db.Plans.Include(p => p.Prices);

        if (!query.IncludeRetired)
            plans = plans.Where(p => p.Prices.Any(pp => pp.IsActive));

        return await plans
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
                    pp.Id, pp.Interval.ToString(), pp.Price, pp.StripePriceId, pp.IsActive)).ToList()))
            .ToListAsync(ct);
    }
}
