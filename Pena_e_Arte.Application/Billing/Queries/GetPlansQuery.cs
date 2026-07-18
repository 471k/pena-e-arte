using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Billing.Queries;

public record GetPlansQuery : IRequest<List<PlanResponse>>;

public class GetPlansHandler(IAppDbContext db)
    : IRequestHandler<GetPlansQuery, List<PlanResponse>>
{
    public async Task<List<PlanResponse>> Handle(GetPlansQuery query, CancellationToken ct)
    {
        return await db.Plans
            .OrderBy(p => p.PriceMonthly)
            .Select(p => new PlanResponse(
                p.Id,
                p.Name,
                p.BillingInterval.ToString(),
                p.PriceMonthly,
                p.PriceYearly,
                p.YearlyDiscountPercent,
                p.AllowBrandingRemoval,
                p.StripePriceIdMonthly,
                p.StripePriceIdYearly,
                db.Subscriptions.Count(s => s.PlanId == p.Id),
                p.MaxArtists,
                p.MaxAppointmentsPerMonth,
                p.MaxNotificationsPerMonth,
                p.MaxStorageGb,
                p.MaxLocations,
                p.AllowApiAccess,
                p.PrioritySupport,
                p.PairedPlanId))
            .ToListAsync(ct);
    }
}
