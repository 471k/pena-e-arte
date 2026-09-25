using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetRevenueRetentionQuery : IRequest<RevenueRetentionResponse>;

/// <summary>
/// GRR/NRR for the last COMPLETED calendar month (a ratio over a half-finished month reads
/// near 100% early on and drifts down, so it is never measured mid-month): start MRR is the
/// ledger's MRR at the last instant of the month before that, and only movements inside the
/// measured month by subscriptions that were already billing at its start count — New and
/// Reactivation are excluded, and so is any expansion/contraction/churn by a subscription that
/// itself only started in that month (it isn't part of the "existing customers" the rate is
/// measured against).
/// </summary>
public class GetRevenueRetentionHandler(IAppDbContext db)
    : IRequestHandler<GetRevenueRetentionQuery, RevenueRetentionResponse>
{
    public async Task<RevenueRetentionResponse> Handle(GetRevenueRetentionQuery query, CancellationToken ct)
    {
        List<SubscriptionRevenueEvent> events = await db.SubscriptionRevenueEvents.ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        DateTime currentMonthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime periodStart = currentMonthStart.AddMonths(-1);
        DateTime startOfPeriod = periodStart.AddTicks(-1);

        decimal startMrr = RevenueLedgerRules.MrrAt(events, startOfPeriod);
        if (startMrr == 0m)
            return new RevenueRetentionResponse(null, null, 0m, periodStart);

        HashSet<Guid> cohort = RevenueLedgerRules.BillingSubscriptionsAt(events, startOfPeriod);
        MrrMovementTotals periodMovements = RevenueLedgerRules.MovementsFor(
            events.Where(e =>
                e.OccurredAt >= periodStart && e.OccurredAt < currentMonthStart
                && cohort.Contains(e.SubscriptionId)));

        // Contraction/Churn are already negative, so adding them subtracts.
        double grr = (double)((startMrr + periodMovements.Contraction + periodMovements.Churn) / startMrr);
        double nrr = (double)((startMrr + periodMovements.Expansion + periodMovements.Contraction + periodMovements.Churn) / startMrr);

        return new RevenueRetentionResponse(grr, nrr, startMrr, periodStart);
    }
}
