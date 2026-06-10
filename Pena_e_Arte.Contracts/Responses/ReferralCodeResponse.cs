namespace Pena_e_Arte.Contracts.Responses;

public record ReferralCodeResponse(
    Guid      Id,
    string    Code,
    string    ShareUrl,
    bool      IsActive,
    bool      IsSingleUse,
    DateTime  CreatedAt,
    DateTime? ExpiresAt);
