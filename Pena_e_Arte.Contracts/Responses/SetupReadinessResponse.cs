namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The owner's setup checklist (E05a, O02; PayModel added by A07a), one entry per area in a fixed
/// order: Artists, DepositRules, Hours, PayModel.
/// </summary>
public record SetupReadinessResponse(IReadOnlyList<SetupAreaResponse> Areas);

/// <param name="Area">"Artists", "DepositRules", "Hours" or "PayModel".</param>
/// <param name="State">"NotConfigured", "DefaultUnreviewed" or "Reviewed".</param>
/// <param name="ReviewedAt">When the owner confirmed it, for areas that record that (Hours, PayModel). Null otherwise.</param>
public record SetupAreaResponse(string Area, string State, DateTime? ReviewedAt);
