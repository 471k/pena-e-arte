namespace Pena_e_Arte.Contracts.Requests;

/// <remarks><c>NextChargeDate</c> is a calendar date (<c>"YYYY-MM-DD"</c>), not an instant: a value with a
/// time or an offset is a 400 at binding, so it can never be zone-converted into the previous day.
/// See <c>BoothRentDates</c>.</remarks>
public record UpdateBoothRentScheduleRequest(
    decimal AmountFixed,
    string Frequency,
    DateOnly NextChargeDate,
    bool IsActive);
