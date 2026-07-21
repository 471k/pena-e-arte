using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CheckSlotAvailabilityHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();
    private readonly Guid          _artistId = Guid.NewGuid();

    private CheckSlotAvailabilityHandler CreateSut() => new(_db);

    private void SeedSchedule(DayOfWeek day)
    {
        _db.ArtistSchedules.Add(new ArtistSchedule
        {
            StudioId    = _studioId,
            ArtistId    = _artistId,
            DayOfWeek   = day,
            StartTime   = TimeSpan.FromHours(9),
            EndTime     = TimeSpan.FromHours(18),
            IsAvailable = true,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Handle_NoClosureAndWithinSchedule_ReturnsAvailable()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(slot.DayOfWeek);

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, slot.AddHours(10), 60), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_StudioClosedThatDay_ReturnsUnavailable()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(slot.DayOfWeek);

        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId  = _studioId,
            StartDate = slot.Date,
            EndDate   = slot.Date,
            Reason    = "Public holiday",
        });
        _db.SaveChanges();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, slot.AddHours(10), 60), default);

        result.Available.Should().BeFalse();
        result.Reason.Should().Be("Studio is closed that day.");
    }

    [Fact]
    public async Task Handle_ClosureOnDifferentDay_DoesNotAffectAvailability()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(slot.DayOfWeek);

        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId  = _studioId,
            StartDate = slot.Date.AddDays(10),
            EndDate   = slot.Date.AddDays(12),
            Reason    = "Renovation",
        });
        _db.SaveChanges();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, slot.AddHours(10), 60), default);

        result.Available.Should().BeTrue();
    }

    private static DateTime NextDateForDay(DayOfWeek day)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }
}
