namespace Pena_e_Arte.Contracts.Responses;

public record BoothRentChargeResponse(
    Guid Id,
    Guid StudioId,
    Guid ArtistId,
    string? ArtistName,
    Guid BoothRentScheduleId,
    decimal Amount,
    DateTime ChargedDate,
    bool IsSettled,
    DateTime? SettledAt,
    string? SettledNote,
    DateTime CreatedAt);
