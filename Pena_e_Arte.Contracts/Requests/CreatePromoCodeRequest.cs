namespace Pena_e_Arte.Contracts.Requests;

public record CreatePromoCodeRequest(
    string Code,
    decimal? AmountFixed,
    decimal? AmountPercent,
    bool IsActive,
    DateTime? ExpiresAt = null,
    int? MaxRedemptions = null);
