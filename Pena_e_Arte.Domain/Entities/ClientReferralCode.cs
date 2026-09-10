namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A client's own shareable referral code, auto-generated on first request
/// (GetOrCreateMyReferralCodeCommand). Distinct from the platform-level ReferralCode
/// (studio-to-studio signup incentive, rewarded via a Stripe coupon on the referring
/// studio's subscription) — this is a client-to-client mechanic scoped to one studio's
/// tenant, redeemed inline at booking time against Flow A (client deposit payment),
/// which has no Stripe coupon surface. See architecture.md Decisions Log,
/// "Client-to-Client Referral Program (P1 #4)".
/// </summary>
public class ClientReferralCode : TenantEntity
{
    public Guid ReferrerClientId { get; set; }

    /// <summary>Unique per studio — see ClientReferralCodeConfiguration's (StudioId, Code) index.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Flat percent off the next deposit, applied to both referrer and referee.
    /// No per-code AmountFixed/AmountPercent choice (unlike PromoCode) — the reward form
    /// is a fixed platform mechanic, not studio-configurable.</summary>
    public decimal RewardPercent { get; set; }

    public int RedemptionCount { get; set; }
}
