namespace Pena_e_Arte.Contracts.Requests;

public record CreateAppointmentRequest(
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    int DurationMinutes,
    string? Notes,
    string TattooDescription = "",
    string? Style = null,
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
    Guid? ReferralRewardId = null,
    // Appended at the end (positional record) to avoid reordering the fields above, which
    // other branches' call sites may already construct positionally. When present, a
    // confirmed PackagePurchase with SessionsRemaining > 0 covers this booking's deposit
    // entirely; see CreateAppointmentCommand. Mutually exclusive with PromoCode/ReferralCode/
    // ReferralRewardId in practice — CreateAppointmentCommand skips all three when a package
    // is used, since the deposit is already 0.
    Guid? PackagePurchaseId = null,
    // When present, CreateAppointmentCommand re-derives DurationMinutes from the service's
    // own DurationMinutes server-side (this field's own DurationMinutes value is ignored in
    // that case, never trusted) and — when the service has its own DepositAmount — that value
    // overrides the DepositRule calculation entirely for this booking. Null preserves the
    // original free-text/no-service booking flow exactly as before this field existed.
    Guid? ServiceId = null);

/// <summary>Category: "AreaPhoto" | "Reference" (matches AppointmentAttachmentCategory).</summary>
public record AppointmentImageRequest(string Url, string Category);
