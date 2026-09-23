using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform.Revenue;

/// <summary>
/// One studio's subscription plus the extra facts MrrRules needs that don't live on
/// Subscription itself: the studio's own TrialExpiresAt (survives conversion — Subscription's
/// copy is nulled), the latest admin-cancellation audit timestamp (Cancelled subscriptions
/// keep a future CurrentPeriodEnd otherwise), and current/latest suspension state (D6).
/// </summary>
public sealed record SubscriptionRevenueInput(
    Subscription Subscription,
    DateTime? StudioTrialExpiresAt,
    DateTime? AdminCancelledAt,
    bool StudioIsActive,
    DateTime? SuspendedAt);

/// <summary>
/// The one definition of MRR — see architecture.md Decisions Log, "One MRR definition
/// (2026-09-23)". Pure and DB-free so both GetPlatformStatsHandler (a single point in time)
/// and GetMrrHistoryHandler (many points in time) agree by construction.
/// </summary>
public static class MrrRules
{
    public static decimal MonthlyEquivalent(Subscription s) =>
        s.Plan?.Prices.FirstOrDefault(pp => pp.Interval == s.BillingInterval) is PlanPrice pp
            ? (pp.Interval == BillingInterval.Monthly ? pp.Price : pp.Price / 12m)
            : 0m;

    /// <summary>
    /// The conservative window (D3) during which this subscription was billing, reconstructed
    /// from current state — can under-count but never over-count. Null when the subscription
    /// never billed anything (no plan, or a status that was never billed: Trialing/GracePeriod).
    /// </summary>
    public static (DateTime Start, DateTime? End)? BillingWindow(SubscriptionRevenueInput input, DateTime now)
    {
        Subscription s = input.Subscription;
        if (s.PlanId is null) return null;
        if (s.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Cancelled))
            return null;

        DateTime start = Min(Max(s.CreatedAt, input.StudioTrialExpiresAt ?? s.CreatedAt), now);

        DateTime? end = s.Status == SubscriptionStatus.Cancelled
            ? (input.AdminCancelledAt is DateTime adminAt ? Min(s.CurrentPeriodEnd, adminAt) : s.CurrentPeriodEnd)
            : null; // Active/PastDue: open — still billing (or was, as of `now`)

        // D6: a suspended studio's window closes at the suspension, whatever its status.
        if (!input.StudioIsActive && input.SuspendedAt is DateTime suspendedAt)
            end = end is DateTime existingEnd ? Min(existingEnd, suspendedAt) : suspendedAt;

        return (start, end);
    }

    /// <summary>
    /// True when this subscription was billing at instant `t`. D2: for the current point
    /// (t == now, always clamped there by callers) this is simply "Active and not suspended" —
    /// no window needed. For any earlier point it falls back to the reconstructed D3 window.
    /// </summary>
    public static bool BillingAt(SubscriptionRevenueInput input, DateTime t, DateTime now)
    {
        if (input.Subscription.PlanId is null) return false;

        if (t >= now)
            return input.Subscription.Status == SubscriptionStatus.Active && input.StudioIsActive;

        (DateTime Start, DateTime? End)? window = BillingWindow(input, now);
        if (window is null) return false;
        (DateTime start, DateTime? end) = window.Value;
        return start <= t && (end is null || end > t);
    }

    public static decimal MrrAt(IEnumerable<SubscriptionRevenueInput> inputs, DateTime t, DateTime now) =>
        inputs.Where(i => BillingAt(i, t, now)).Sum(i => MonthlyEquivalent(i.Subscription));

    /// <summary>PastDue, now — not in headline MRR, reported on its own line.</summary>
    public static decimal AtRiskMrr(IEnumerable<SubscriptionRevenueInput> inputs) =>
        inputs
            .Where(i => i.Subscription.Status == SubscriptionStatus.PastDue && i.StudioIsActive)
            .Sum(i => MonthlyEquivalent(i.Subscription));

    /// <summary>Active + CancelAtPeriodEnd, now — still counted in headline MRR (D1), also reported separately.</summary>
    public static decimal ScheduledChurnMrr(IEnumerable<SubscriptionRevenueInput> inputs) =>
        inputs
            .Where(i => i.Subscription.Status == SubscriptionStatus.Active
                        && i.Subscription.CancelAtPeriodEnd
                        && i.StudioIsActive)
            .Sum(i => MonthlyEquivalent(i.Subscription));

    /// <summary>Active or PastDue, studio suspended (D6) — excluded from headline MRR, reported on its own line.</summary>
    public static decimal PausedMrr(IEnumerable<SubscriptionRevenueInput> inputs) =>
        inputs
            .Where(i => i.Subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue
                        && !i.StudioIsActive)
            .Sum(i => MonthlyEquivalent(i.Subscription));

    /// <summary>Active, not suspended, monthly-equivalent above zero — the ARPA denominator (D4).</summary>
    public static int PayingStudios(IEnumerable<SubscriptionRevenueInput> inputs) =>
        inputs.Count(i =>
            i.Subscription.Status == SubscriptionStatus.Active
            && i.StudioIsActive
            && MonthlyEquivalent(i.Subscription) > 0);

    public static DateTime EndOfMonth(DateTime monthStart) => monthStart.AddMonths(1).AddTicks(-1);

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
