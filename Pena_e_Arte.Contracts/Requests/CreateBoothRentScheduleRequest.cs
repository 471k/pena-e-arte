namespace Pena_e_Arte.Contracts.Requests;

public record CreateBoothRentScheduleRequest(
    Guid ArtistId,
    decimal AmountFixed,
    string Frequency, // "Weekly" | "Monthly"
    // A calendar date ("YYYY-MM-DD"), not an instant: a value with a time or an offset is a 400 at
    // binding, so it can never be zone-converted into the previous day. See BoothRentDates.
    DateOnly NextChargeDate,
    bool IsActive);
