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
            .Include(p => p.Prices)
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
                p.Prices.Select(pp => new PlanPriceResponse(
                    pp.Id, pp.Interval.ToString(), pp.Price, pp.StripePriceId, pp.IsActive)).ToList()))
            .ToListAsync(ct);
    }
}
