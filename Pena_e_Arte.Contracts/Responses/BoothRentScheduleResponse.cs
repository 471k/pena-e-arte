namespace Pena_e_Arte.Contracts.Responses;

public record BoothRentScheduleResponse(
    Guid Id,
    Guid StudioId,
    Guid ArtistId,
    string? ArtistName,
    decimal AmountFixed,
    string Frequency,
    DateTime NextChargeDate,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
