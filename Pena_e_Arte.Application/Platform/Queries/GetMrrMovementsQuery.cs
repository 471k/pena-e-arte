using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetMrrMovementsQuery(int Months = 12) : IRequest<List<MrrMovementsDataPointResponse>>;

/// <summary>
/// Monthly New/Expansion/Reactivation/Contraction/Churn totals, straight from the revenue
/// ledger. Unlike the MRR trend there is no estimated-mode fallback — a month that predates the
/// ledger has no events, so it reads as all zeros (indistinguishable from a genuinely quiet
/// month, which is acceptable: the platform pre-dates the ledger by design).
/// </summary>
public class GetMrrMovementsHandler(IAppDbContext db)
    : IRequestHandler<GetMrrMovementsQuery, List<MrrMovementsDataPointResponse>>
{
    public async Task<List<MrrMovementsDataPointResponse>> Handle(GetMrrMovementsQuery query, CancellationToken ct)
    {
        int months = Math.Clamp(query.Months, 1, 24);

        List<SubscriptionRevenueEvent> events = await db.SubscriptionRevenueEvents.ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        var result = new List<MrrMovementsDataPointResponse>(months);

        for (int i = months - 1; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            MrrMovementTotals totals = RevenueLedgerRules.MovementsFor(
                events.Where(e => e.OccurredAt >= monthStart && e.OccurredAt < nextMonthStart));

            result.Add(new MrrMovementsDataPointResponse(
                monthStart.ToString("yyyy-MM"),
                totals.New, totals.Expansion, totals.Reactivation, totals.Contraction, totals.Churn, totals.Net));
        }

        return result;
    }
}
