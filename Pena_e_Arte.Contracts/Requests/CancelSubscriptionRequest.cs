namespace Pena_e_Arte.Contracts.Requests;

/// <summary>Admin subscription-cancel override. Override is null for the default (formula)
/// path — the one-click list-row confirm. "AdminFull"/"AdminNone" require Reason.</summary>
public record CancelSubscriptionRequest(string? Override, string? Reason);
