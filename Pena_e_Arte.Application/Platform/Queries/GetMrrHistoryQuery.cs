using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetMrrHistoryQuery(int Months = 12) : IRequest<List<MrrDataPointResponse>>;

public class GetMrrHistoryHandler(IAppDbContext db, ILogger<GetMrrHistoryHandler> logger)
    : IRequestHandler<GetMrrHistoryQuery, List<MrrDataPointResponse>>
{
    public async Task<List<MrrDataPointResponse>> Handle(GetMrrHistoryQuery query, CancellationToken ct)
    {
        int months = Math.Clamp(query.Months, 1, 24);

        List<SubscriptionRevenueInput> inputs = await MrrInputLoader.LoadAsync(db, ct);

        int fallbackCount = inputs.Count(i => i.Subscription.BilledUnitAmount is null && i.Subscription.PlanId is not null);
        int currencyExcludedCount = inputs.Count(i =>
            i.Subscription.BilledCurrency is string c && c != MrrRules.PlatformCurrency);
        if (fallbackCount > 0 || currencyExcludedCount > 0)
        {
            logger.LogInformation(
                "MRR computed with {FallbackCount} subscription(s) on the pre-snapshot PlanPrice fallback " +
                "and {CurrencyExcludedCount} excluded for non-platform currency",
                fallbackCount, currencyExcludedCount);
        }

        // Recorded history: months on or after the ledger's first event read the ledger; earlier
        // months keep Batch 2's reconstruction from current state, flagged as estimated. Loaded
        // once and reused across the loop, same as the reconstruction inputs above.
        List<SubscriptionRevenueEvent> ledgerEvents = await db.SubscriptionRevenueEvents.ToListAsync(ct);
        DateTime? ledgerStart = ledgerEvents.Count == 0 ? null : ledgerEvents.Min(e => e.OccurredAt);

        DateTime now = DateTime.UtcNow;
        var result = new List<MrrDataPointResponse>(months);

        for (int i = months - 1; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);

            // D2: the current month is measured now; every earlier month at its own last moment.
            DateTime t = i == 0 ? now : MrrRules.EndOfMonth(monthStart);

            bool isRecorded = ledgerStart is DateTime start && t >= start;
            decimal mrr = isRecorded
                ? RevenueLedgerRules.MrrAt(ledgerEvents, t)
                : MrrRules.MrrAt(inputs, t, now);

            result.Add(new MrrDataPointResponse(monthStart.ToString("yyyy-MM"), mrr, IsEstimated: !isRecorded));
        }

        return result;
    }
}
