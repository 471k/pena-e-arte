using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetMrrHistoryQuery(int Months = 12) : IRequest<List<MrrDataPointResponse>>;

public class GetMrrHistoryHandler(IAppDbContext db)
    : IRequestHandler<GetMrrHistoryQuery, List<MrrDataPointResponse>>
{
    public async Task<List<MrrDataPointResponse>> Handle(GetMrrHistoryQuery query, CancellationToken ct)
    {
        int months = Math.Clamp(query.Months, 1, 24);

        // AdminOnly endpoint — no tenant filter on Subscriptions entity (not a TenantEntity).
        var subscriptions = await db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
                .ThenInclude(p => p!.Prices)
            .Where(s => s.Plan != null)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        var result = new List<MrrDataPointResponse>(months);

        for (int i = months - 1; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            DateTime monthEnd = monthStart.AddMonths(1);

            decimal mrr = subscriptions
                .Where(s => s.CreatedAt < monthEnd && s.CurrentPeriodEnd >= monthStart)
                .Sum(MonthlyEquivalentRevenue);

            result.Add(new MrrDataPointResponse(monthStart.ToString("yyyy-MM"), mrr));
        }

        return result;
    }

    private static decimal MonthlyEquivalentRevenue(Subscription s) =>
        s.Plan?.Prices.FirstOrDefault(pp => pp.Interval == s.BillingInterval) is PlanPrice pp
            ? (pp.Interval == BillingInterval.Monthly ? pp.Price : pp.Price / 12m)
            : 0m;
}
