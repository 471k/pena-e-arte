namespace Pena_e_Arte.Contracts.Responses;

public record SavedPaymentMethodResponse(
    Guid Id,
    string? CardBrand,
    string? MaskedPan,
    string? ExpiryMonth,
    string? ExpiryYear,
    bool IsDefault,
    DateTime CreatedAt);
