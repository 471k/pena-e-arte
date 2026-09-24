using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetRevenueRetentionQuery : IRequest<RevenueRetentionResponse>;

/// <summary>
/// GRR/NRR for the current calendar month so far: start MRR is the ledger's MRR at the last
/// instant of the previous month, and only movements by subscriptions that were already billing
/// then count — New and Reactivation are excluded, and so is any expansion/contraction/churn by
/// a subscription that itself only started this month (it isn't part of the "existing customers"
/// the rate is measured against).
/// </summary>
public class GetRevenueRetentionHandler(IAppDbContext db)
    : IRequestHandler<GetRevenueRetentionQuery, RevenueRetentionResponse>
{
    public async Task<RevenueRetentionResponse> Handle(GetRevenueRetentionQuery query, CancellationToken ct)
    {
        List<SubscriptionRevenueEvent> events = await db.SubscriptionRevenueEvents.ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime startOfPeriod = monthStart.AddTicks(-1);

        decimal startMrr = RevenueLedgerRules.MrrAt(events, startOfPeriod);
        if (startMrr == 0m)
            return new RevenueRetentionResponse(null, null, 0m);

        HashSet<Guid> cohort = RevenueLedgerRules.BillingSubscriptionsAt(events, startOfPeriod);
        MrrMovementTotals thisMonth = RevenueLedgerRules.MovementsFor(
            events.Where(e => e.OccurredAt >= monthStart && cohort.Contains(e.SubscriptionId)));

        // Contraction/Churn are already negative, so adding them subtracts.
        double grr = (double)((startMrr + thisMonth.Contraction + thisMonth.Churn) / startMrr);
        double nrr = (double)((startMrr + thisMonth.Expansion + thisMonth.Contraction + thisMonth.Churn) / startMrr);

        return new RevenueRetentionResponse(grr, nrr, startMrr);
    }
}
