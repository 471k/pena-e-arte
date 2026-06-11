namespace Pena_e_Arte.Contracts.Responses;

public record ReferralStatsResponse(
    string? Code,
    int     RedemptionCount,
    int     DiscountsApplied);
