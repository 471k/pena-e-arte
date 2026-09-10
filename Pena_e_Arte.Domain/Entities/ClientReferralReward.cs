namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// The referrer's own earned credit — created automatically when someone redeems their
/// ClientReferralCode. Structurally a single-use, single-client-scoped percent-off reward,
/// spent by the referrer on their own future booking (ReferralRewardId on
/// CreateAppointmentRequest) rather than applied immediately, since the referrer isn't
/// necessarily booking anything at the moment their code gets redeemed.
/// </summary>
public class ClientReferralReward : TenantEntity
{
    /// <summary>The referrer who earned this.</summary>
    public Guid ClientId { get; set; }

    /// <summary>Which redemption earned it.</summary>
    public Guid SourceRedemptionId { get; set; }

    public decimal RewardPercent { get; set; }
    public bool IsRedeemed { get; set; }
    public Guid? RedeemedOnAppointmentId { get; set; }
}
