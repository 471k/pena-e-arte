namespace Pena_e_Arte.Contracts.Requests;

public record ActivateSubscriptionManuallyRequest(Guid PlanId, string? Note);
