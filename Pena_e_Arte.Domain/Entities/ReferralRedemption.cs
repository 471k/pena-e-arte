namespace Pena_e_Arte.Domain.Entities;

public class ReferralRedemption
{
    public Guid     Id               { get; init; } = Guid.NewGuid();
    public Guid     ReferralCodeId   { get; set; }
    public Guid     NewStudioId      { get; set; }
    public DateTime RedeemedAt       { get; init; } = DateTime.UtcNow;
    public bool     DiscountApplied  { get; set; }

    /// <summary>True once the referring studio has received their reward coupon. Guards
    /// idempotency: both subscription-creation paths call RewardReferrerAsync, so this
    /// flag ensures a retry never issues a second coupon.</summary>
    public bool     ReferrerRewardApplied  { get; set; }

    /// <summary>Stripe coupon ID issued to the referrer, for audit/support use.</summary>
    public string?  ReferrerRewardCouponId { get; set; }
}
