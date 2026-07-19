using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Subscription
{
    public Guid               Id                   { get; init; } = Guid.NewGuid();
    public Guid                StudioId             { get; set; }
    public Guid?                PlanId               { get; set; }

    /// <summary>Which cadence this subscription is actually billed on. Independent of
    /// PlanId — see architecture.md Decisions Log, "Plan/PlanPrice split".</summary>
    public BillingInterval      BillingInterval      { get; set; } = BillingInterval.Monthly;

    /// <summary>Plan a scheduled downgrade switches to at the end of the current period. Null when no change is pending.</summary>
    public Guid?                PendingPlanId        { get; set; }

    /// <summary>Interval that PendingPlanId will apply under, once it lands. Set and
    /// cleared together with PendingPlanId — always both null or both non-null.</summary>
    public BillingInterval?     PendingBillingInterval { get; set; }

    public SubscriptionStatus Status               { get; set; }

    /// <summary>Null once the subscription converts to a paid plan — trial is no longer applicable.</summary>
    public DateTime?          TrialExpiresAt       { get; set; }
    public DateTime           CurrentPeriodEnd     { get; set; }
    public DateTime           GracePeriodEnd       { get; set; }
    public string?            StripeSubscriptionId { get; set; }
    public DateTime           CreatedAt            { get; init; } = DateTime.UtcNow;

    public Studio Studio { get; set; } = null!;
    public Plan?  Plan   { get; set; }
}
