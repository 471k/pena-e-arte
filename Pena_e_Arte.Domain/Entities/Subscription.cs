using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Subscription
{
    public Guid               Id                   { get; init; } = Guid.NewGuid();
    public Guid               StudioId             { get; set; }
    public Guid?              PlanId               { get; set; }

    /// <summary>Plan a scheduled downgrade switches to at the end of the current period. Null when no change is pending.</summary>
    public Guid?              PendingPlanId        { get; set; }

    public SubscriptionStatus Status               { get; set; }
    public DateTime           TrialExpiresAt       { get; set; }
    public DateTime           CurrentPeriodEnd     { get; set; }
    public DateTime           GracePeriodEnd       { get; set; }
    public string?            StripeSubscriptionId { get; set; }
    public DateTime           CreatedAt            { get; init; } = DateTime.UtcNow;

    public Studio Studio { get; set; } = null!;
    public Plan?  Plan   { get; set; }
}
