namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Applies a reward coupon to the referring studio's active Stripe subscription
/// when their referral code successfully converts a new paying studio.
/// </summary>
public interface IReferralRewardService
{
    /// <summary>
    /// Issues a one-month-free coupon to the referring studio for a completed,
    /// discount-applied referral redemption. Idempotent — safe to call more than
    /// once for the same <paramref name="referralRedemptionId"/>; subsequent calls
    /// are no-ops once <c>ReferrerRewardApplied</c> is true.
    /// </summary>
    Task RewardReferrerAsync(Guid referralRedemptionId, CancellationToken ct);
}
