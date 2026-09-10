namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Records a referee redeeming someone else's ClientReferralCode on their own booking.
/// DB-enforced (not just application-checked) one-redemption-per-client-per-code via the
/// unique (ClientReferralCodeId, RedeemedByClientId) index — same "DB-enforced, not just
/// application-checked" posture Payment.AppointmentId's unique index already models.
/// </summary>
public class ClientReferralRedemption : TenantEntity
{
    public Guid ClientReferralCodeId { get; set; }
    public Guid RedeemedByClientId { get; set; }

    /// <summary>The referee's booking the reward applied to.</summary>
    public Guid AppointmentId { get; set; }
}
