namespace Pena_e_Arte.Contracts.Responses;

public record BookingDraftResponse(
    string Payload,
    List<BookingDraftImageResponse> Images,
    DateTime SavedAt,
    DateTime ExpiresAt);

public record BookingDraftImageResponse(string Url, string Category);
