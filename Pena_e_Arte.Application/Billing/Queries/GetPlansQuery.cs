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
                p.StripePriceIdMonthly,
                p.StripePriceIdYearly))
            .ToListAsync(ct);
    }
}
