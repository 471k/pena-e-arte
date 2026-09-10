namespace Pena_e_Arte.Contracts.Responses;

public record ClientReferralRewardResponse(
    Guid Id,
    decimal RewardPercent,
    bool IsRedeemed,
    DateTime CreatedAt);
