namespace Pena_e_Arte.Contracts.Responses;

public record PlatformReferralCodeResponse(
    Guid      Id,
    Guid      StudioId,
    string    StudioName,
    string    Code,
    string    ShareUrl,
    bool      IsActive,
    bool      IsSingleUse,
    DateTime  CreatedAt,
    DateTime? ExpiresAt,
    int       RedemptionCount);
