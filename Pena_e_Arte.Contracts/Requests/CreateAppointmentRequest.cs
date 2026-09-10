namespace Pena_e_Arte.Contracts.Requests;

public record CreateAppointmentRequest(
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    int DurationMinutes,
    string? Notes,
    string TattooDescription = "",
    string? SafetyNotes = null,
    IReadOnlyList<string>? DesiredPlacementLocations = null,
    string? ReferralSource = null,          // enum name as string, nullable — "Other" requires ReferralSourceOther
    string? ReferralSourceOther = null,
    IReadOnlyList<AppointmentImageRequest>? Images = null,
    string? PromoCode = null,
    // Reward-bearing client referral — distinct from ReferralSource ("how did you hear
    // about us" marketing attribution) above. ReferralCode redeems someone else's
    // ClientReferralCode; ReferralRewardId redeems the caller's own earned credit. Both
    // optional, mutually exclusive in practice (a booking client either redeems someone
    // else's code or spends their own earned reward, not both).
    string? ReferralCode = null,
    Guid? ReferralRewardId = null);

/// <summary>Category: "AreaPhoto" | "Reference" (matches AppointmentAttachmentCategory).</summary>
public record AppointmentImageRequest(string Url, string Category);
