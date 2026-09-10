namespace Pena_e_Arte.Contracts.Requests;

public record StudioHoursEntryRequest(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsOpen);

public record UpsertStudioHoursRequest(IReadOnlyList<StudioHoursEntryRequest> Entries);
