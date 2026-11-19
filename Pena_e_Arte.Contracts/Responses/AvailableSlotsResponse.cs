namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// C03 (slice 7): bookable starts, a preview and not a reservation. Days are studio-local dates; starts are UTC
/// instants to show on the studio's clock. NextAvailable is the first start at or after From within 60 days, or null.
/// </summary>
public record AvailableSlotsResponse(
    DateOnly From,
    DateOnly To,
    int DurationMinutes,
    int StepMinutes,
    IReadOnlyList<AvailableSlotsDayResponse> Days,
    DateTime? NextAvailable);

public record AvailableSlotsDayResponse(DateOnly Date, IReadOnlyList<DateTime> Starts);
