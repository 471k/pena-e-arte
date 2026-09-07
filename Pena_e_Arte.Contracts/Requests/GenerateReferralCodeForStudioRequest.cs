namespace Pena_e_Arte.Contracts.Requests;

/// <summary>Optional body for admin-initiated referral code generation.</summary>
public record GenerateReferralCodeForStudioRequest(DateTime? ExpiresAt = null);
