namespace Pena_e_Arte.Contracts.Requests;

public record UpdateBoothRentScheduleRequest(
    decimal AmountFixed,
    string Frequency,
    DateTime NextChargeDate,
    bool IsActive);
