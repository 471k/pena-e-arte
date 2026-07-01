namespace Pena_e_Arte.Contracts.Requests;

public record ScheduleEntryRequest(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsAvailable);

public record UpsertArtistScheduleRequest(IReadOnlyList<ScheduleEntryRequest> Entries);
