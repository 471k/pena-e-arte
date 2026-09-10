namespace Pena_e_Arte.Contracts.Responses;

public record ClientReferralCodeResponse(
    Guid Id,
    string Code,
    string ShareUrl,
    decimal RewardPercent,
    int RedemptionCount);
