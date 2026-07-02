using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class UpsertArtistScheduleValidatorTests
{
    private readonly UpsertArtistScheduleValidator _sut = new();

    [Fact]
    public void Validate_ValidEntries_IsValid()
    {
        _sut.ShouldBeValid(new UpsertArtistScheduleCommand(Guid.NewGuid(),
        [
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true),
            new(DayOfWeek.Tuesday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true),
        ]));
    }

    [Fact]
    public void Validate_MoreThanSevenEntries_FailsOnEntries()
    {
        List<ScheduleEntryDto> entries = Enumerable.Range(0, 8)
            .Select(i => new ScheduleEntryDto((DayOfWeek)(i % 7), TimeSpan.FromHours(9), TimeSpan.FromHours(17), true))
            .ToList();

        _sut.ShouldFailOn(new UpsertArtistScheduleCommand(Guid.NewGuid(), entries), "Entries");
    }

    [Fact]
    public void Validate_DuplicateDayOfWeek_FailsOnEntries()
    {
        _sut.ShouldFailOn(new UpsertArtistScheduleCommand(Guid.NewGuid(),
        [
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true),
            new(DayOfWeek.Monday, TimeSpan.FromHours(10), TimeSpan.FromHours(18), true),
        ]), "Entries");
    }

    [Fact]
    public void Validate_StartTimeAfterEndTime_FailsOnEntry()
    {
        _sut.ShouldFailOn(new UpsertArtistScheduleCommand(Guid.NewGuid(),
        [
            new(DayOfWeek.Monday, TimeSpan.FromHours(17), TimeSpan.FromHours(9), true),
        ]), "Entries[0].StartTime");
    }
}
