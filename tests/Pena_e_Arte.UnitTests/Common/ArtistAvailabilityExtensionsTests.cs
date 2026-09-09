using FluentAssertions;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Common;

// No pre-existing test file covered ArtistAvailabilityExtensions before the StudioHours hard
// gate (2026-09-09) — this file is new, not an extension of an existing suite, despite the
// P1 Group 2 overnight prompt's assumption that one already existed.
public class ArtistAvailabilityExtensionsTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    private void SeedActiveArtist()
    {
        _db.Artists.Add(new Artist
        {
            Id = _artistId,
            StudioId = _studioId,
            FirstName = "Any",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@artist.test",
            IsActive = true,
        });
        _db.SaveChanges();
    }

    private void SeedArtistSchedule(DayOfWeek day, TimeSpan? start = null, TimeSpan? end = null)
    {
        _db.ArtistSchedules.Add(new ArtistSchedule
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            DayOfWeek = day,
            StartTime = start ?? TimeSpan.Zero,
            EndTime = end ?? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
            IsAvailable = true,
        });
        _db.SaveChanges();
    }

    private void SeedStudioHours(DayOfWeek day, TimeSpan start, TimeSpan end, bool isOpen = true)
    {
        _db.StudioHours.Add(new StudioHours
        {
            StudioId = _studioId,
            DayOfWeek = day,
            StartTime = start,
            EndTime = end,
            IsOpen = isOpen,
        });
        _db.SaveChanges();
    }

    private static DateTime NextDateForDay(DayOfWeek day)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    // ── IsAnyArtistAvailableAsync ────────────────────────────────────────────

    [Fact]
    public async Task IsAnyArtistAvailableAsync_NoStudioHoursRowForDay_ReturnsFalse()
    {
        SeedActiveArtist();
        SeedArtistSchedule(DayOfWeek.Monday);
        // No StudioHours row for Monday at all.
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(10);

        bool available = await _db.IsAnyArtistAvailableAsync(_studioId, slot, 60, default);

        available.Should().BeFalse();
    }

    [Fact]
    public async Task IsAnyArtistAvailableAsync_StudioHoursIsOpenFalse_ReturnsFalse()
    {
        SeedActiveArtist();
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.FromHours(23), isOpen: false);
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(10);

        bool available = await _db.IsAnyArtistAvailableAsync(_studioId, slot, 60, default);

        available.Should().BeFalse();
    }

    [Fact]
    public async Task IsAnyArtistAvailableAsync_SlotOutsideStudioHours_ReturnsFalse()
    {
        SeedActiveArtist();
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18));
        // 19:00 is outside the 09:00–18:00 studio hours.
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(19);

        bool available = await _db.IsAnyArtistAvailableAsync(_studioId, slot, 60, default);

        available.Should().BeFalse();
    }

    [Fact]
    public async Task IsAnyArtistAvailableAsync_SlotInsideStudioHoursAndEverythingElseClear_ReturnsTrue()
    {
        SeedActiveArtist();
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18));
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(10);

        bool available = await _db.IsAnyArtistAvailableAsync(_studioId, slot, 60, default);

        available.Should().BeTrue();
    }

    [Fact]
    public async Task IsAnyArtistAvailableAsync_StudioClosureStillShortCircuitsBeforeStudioHours()
    {
        SeedActiveArtist();
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.FromHours(23));
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId = _studioId,
            StartDate = slot.Date,
            EndDate = slot.Date,
            Reason = "Public holiday",
        });
        _db.SaveChanges();

        bool available = await _db.IsAnyArtistAvailableAsync(_studioId, slot.AddHours(10), 60, default);

        available.Should().BeFalse();
    }

    // ── CheckArtistScheduleAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CheckArtistScheduleAsync_NoStudioHoursRowForDay_ReturnsUnavailable()
    {
        SeedArtistSchedule(DayOfWeek.Monday);
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(10);

        (bool available, string? reason) =
            await _db.CheckArtistScheduleAsync(_studioId, _artistId, slot, 60, default);

        available.Should().BeFalse();
        reason.Should().Be("Studio is closed that day.");
    }

    [Fact]
    public async Task CheckArtistScheduleAsync_StudioHoursIsOpenFalse_ReturnsUnavailable()
    {
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.FromHours(23), isOpen: false);
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(10);

        (bool available, string? reason) =
            await _db.CheckArtistScheduleAsync(_studioId, _artistId, slot, 60, default);

        available.Should().BeFalse();
        reason.Should().Be("Studio is closed that day.");
    }

    [Fact]
    public async Task CheckArtistScheduleAsync_SlotOutsideStudioHours_ReturnsUnavailableWithReason()
    {
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18));
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(19);

        (bool available, string? reason) =
            await _db.CheckArtistScheduleAsync(_studioId, _artistId, slot, 60, default);

        available.Should().BeFalse();
        reason.Should().Be("Outside studio hours (09:00–18:00).");
    }

    [Fact]
    public async Task CheckArtistScheduleAsync_SlotInsideStudioHoursAndEverythingElseClear_ReturnsAvailable()
    {
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18));
        DateTime slot = NextDateForDay(DayOfWeek.Monday).AddHours(10);

        (bool available, string? reason) =
            await _db.CheckArtistScheduleAsync(_studioId, _artistId, slot, 60, default);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public async Task CheckArtistScheduleAsync_ExistingClosureOnlyTestCase_StillPasses()
    {
        // Confirms the pre-existing StudioClosures-only behavior is unmodified: closure still
        // wins/short-circuits before StudioHours is even checked.
        SeedArtistSchedule(DayOfWeek.Monday);
        SeedStudioHours(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.FromHours(23));
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId = _studioId,
            StartDate = slot.Date,
            EndDate = slot.Date,
            Reason = "Renovation",
        });
        _db.SaveChanges();

        (bool available, string? reason) =
            await _db.CheckArtistScheduleAsync(_studioId, _artistId, slot.AddHours(10), 60, default);

        available.Should().BeFalse();
        reason.Should().Be("Studio is closed that day.");
    }
}
