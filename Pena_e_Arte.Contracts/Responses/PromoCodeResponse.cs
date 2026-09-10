namespace Pena_e_Arte.Contracts.Responses;

public record PromoCodeResponse(
    Guid Id,
    Guid StudioId,
    string Code,
    decimal? AmountFixed,
    decimal? AmountPercent,
    bool IsActive,
    DateTime? ExpiresAt,
    int? MaxRedemptions,
    int RedemptionCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
