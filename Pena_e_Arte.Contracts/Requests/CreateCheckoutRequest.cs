namespace Pena_e_Arte.Contracts.Requests;

public record CreateCheckoutRequest(Guid PlanId, string BillingInterval, string SuccessUrl, string CancelUrl);
