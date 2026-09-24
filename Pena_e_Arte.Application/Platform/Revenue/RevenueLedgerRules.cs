using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform.Revenue;

/// <summary>
/// Ledger-based MRR — pure and DB-free, mirroring MrrRules. Where MrrRules reconstructs a past
/// month from current subscription state, these read what was actually recorded. For a fully
/// up-to-date ledger, MrrAt(events, now) equals MrrRules.MrrAt(inputs, now, now).
/// </summary>
public static class RevenueLedgerRules
{
    // Types that represent an actively-billing state. Resumed is deliberately included — it
    // hands back the contracted MrrAfter that the preceding Paused row left excluded.
    private static readonly HashSet<RevenueEventType> BillingTypes =
    [
        RevenueEventType.New, RevenueEventType.Expansion, RevenueEventType.Contraction,
        RevenueEventType.Reactivation, RevenueEventType.Recovered, RevenueEventType.Resumed,
    ];

    /// <summary>The latest event at-or-before t, per subscription, counts toward MRR only when
    /// its Type represents an actively-billing state. Ties on OccurredAt are broken by
    /// CreatedAt (insertion order) — never by Id, which is a random Guid.</summary>
    public static decimal MrrAt(IEnumerable<SubscriptionRevenueEvent> events, DateTime t) =>
        events
            .Where(e => e.OccurredAt <= t)
            .GroupBy(e => e.SubscriptionId)
            .Select(g => g.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.CreatedAt).First())
            .Where(latest => BillingTypes.Contains(latest.Type))
            .Sum(latest => latest.MrrAfter);

    // Two webhook/request handlers can act on the same real-world transition at the same instant
    // (observed with real Stripe test-mode webhooks: invoice.paid and customer.subscription.updated
    // for one recovered payment arrived in the same second, both read the row as PastDue before
    // either committed, and both wrote a Recovered). The ledger is append-only, so the duplicate row
    // stays, but it must not be counted twice in a movement total. Rows for the same subscription,
    // type and amounts within this window are one movement.
    private static readonly TimeSpan SimultaneousDuplicateWindow = TimeSpan.FromSeconds(60);

    private static List<SubscriptionRevenueEvent> CollapseNearSimultaneousDuplicates(
        IEnumerable<SubscriptionRevenueEvent> events)
    {
        List<SubscriptionRevenueEvent> kept = [];
        foreach (SubscriptionRevenueEvent e in events.OrderBy(x => x.OccurredAt).ThenBy(x => x.CreatedAt))
        {
            bool duplicate = kept.Any(k =>
                k.SubscriptionId == e.SubscriptionId
                && k.Type == e.Type
                && k.MrrBefore == e.MrrBefore
                && k.MrrAfter == e.MrrAfter
                && e.OccurredAt - k.OccurredAt <= SimultaneousDuplicateWindow);
            if (!duplicate) kept.Add(e);
        }
        return kept;
    }

    /// <summary>The subscriptions actively billing (with MRR above zero) at instant t — the
    /// "existing customers" cohort a retention rate is measured against.</summary>
    public static HashSet<Guid> BillingSubscriptionsAt(IEnumerable<SubscriptionRevenueEvent> events, DateTime t) =>
        events
            .Where(e => e.OccurredAt <= t)
            .GroupBy(e => e.SubscriptionId)
            .Select(g => g.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.CreatedAt).First())
            .Where(latest => BillingTypes.Contains(latest.Type) && latest.MrrAfter > 0m)
            .Select(latest => latest.SubscriptionId)
            .ToHashSet();

    /// <summary>Movement totals for one calendar month — only the five ChartMogul/Baremetrics
    /// movement types feed this; Paused/Resumed/PastDue/Recovered are state, not MRR movement,
    /// and are excluded here even though they're real ledger rows.</summary>
    public static MrrMovementTotals MovementsFor(IEnumerable<SubscriptionRevenueEvent> monthEvents)
    {
        List<SubscriptionRevenueEvent> events = CollapseNearSimultaneousDuplicates(monthEvents).ToList();

        decimal Sum(RevenueEventType type) =>
            events.Where(e => e.Type == type).Sum(e => e.MrrAfter - e.MrrBefore);

        decimal newMrr = Sum(RevenueEventType.New);
        decimal expansion = Sum(RevenueEventType.Expansion);
        decimal reactivation = Sum(RevenueEventType.Reactivation);
        decimal contraction = Sum(RevenueEventType.Contraction); // already negative (MrrAfter < MrrBefore)
        decimal churn = Sum(RevenueEventType.Churn);             // already negative (MrrAfter == 0)

        return new MrrMovementTotals(newMrr, expansion, reactivation, contraction, churn,
            newMrr + expansion + reactivation + contraction + churn);
    }
}

public sealed record MrrMovementTotals(
    decimal New, decimal Expansion, decimal Reactivation, decimal Contraction, decimal Churn, decimal Net);
