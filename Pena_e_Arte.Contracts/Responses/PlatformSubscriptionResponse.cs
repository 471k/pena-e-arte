namespace Pena_e_Arte.Contracts.Responses;

public record PlatformSubscriptionResponse(
    Guid StudioId,
    string StudioName,
    string StudioSlug,
    Guid? SubscriptionId,
    string Status,
    string? PlanName,
    DateTime? TrialExpiresAt,
    DateTime CurrentPeriodEnd,
    bool IsSuspended,
    bool CancelAtPeriodEnd = false);
