namespace Pena_e_Arte.Contracts.Responses;

public record SubscriptionResponse(
    Guid      Id,
    Guid      StudioId,
    Guid?     PlanId,
    string    BillingInterval,
    Guid?     PendingPlanId,
    string?   PendingBillingInterval,
    string    Status,
    DateTime? TrialExpiresAt,
    DateTime  CurrentPeriodEnd,
    DateTime  GracePeriodEnd,
    string?   StripeSubscriptionId,
    bool      CancelAtPeriodEnd = false);
