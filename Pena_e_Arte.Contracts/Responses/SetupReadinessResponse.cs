namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The owner's setup checklist (E05a, O02), one entry per area in a fixed order:
/// Artists, DepositRules, Hours.
/// </summary>
public record SetupReadinessResponse(IReadOnlyList<SetupAreaResponse> Areas);

/// <param name="Area">"Artists", "DepositRules" or "Hours".</param>
/// <param name="State">"NotConfigured", "DefaultUnreviewed" or "Reviewed".</param>
/// <param name="ReviewedAt">When the owner confirmed it, for areas that record that (Hours). Null otherwise.</param>
public record SetupAreaResponse(string Area, string State, DateTime? ReviewedAt);
