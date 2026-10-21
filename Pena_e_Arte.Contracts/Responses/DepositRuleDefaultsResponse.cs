namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// What applies when a studio has no active deposit rule, or its rule leaves a field empty.
/// Served from the backend constants so the frontend never keeps its own copy.
/// </summary>
public record DepositRuleDefaultsResponse(int CancellationWindowHours, int RefundPercentOnLateCancel);
