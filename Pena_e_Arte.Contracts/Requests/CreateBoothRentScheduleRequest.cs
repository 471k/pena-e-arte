namespace Pena_e_Arte.Contracts.Requests;

public record CreateBoothRentScheduleRequest(
    Guid ArtistId,
    decimal AmountFixed,
    string Frequency, // "Weekly" | "Monthly"
    DateTime NextChargeDate,
    bool IsActive);
