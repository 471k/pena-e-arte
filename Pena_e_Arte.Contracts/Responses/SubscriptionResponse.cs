namespace Pena_e_Arte.Contracts.Responses;

public record SubscriptionResponse(
    Guid      Id,
    Guid      StudioId,
    Guid?     PlanId,
    Guid?     PendingPlanId,
    string    Status,
    DateTime? TrialExpiresAt,
    DateTime  CurrentPeriodEnd,
    DateTime  GracePeriodEnd,
    string?   StripeSubscriptionId);
