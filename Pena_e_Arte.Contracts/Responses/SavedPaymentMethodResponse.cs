namespace Pena_e_Arte.Contracts.Responses;

public record SavedPaymentMethodResponse(
    Guid Id,
    string? CardBrand,
    string? MaskedPan,
    string? ExpiryMonth,
    string? ExpiryYear,
    bool IsDefault,
    DateTime CreatedAt,
    // Only set by the add endpoint: true when this card was already on file and the existing
    // entry was kept (refreshed) instead of adding a second copy.
    bool AlreadySaved = false);
