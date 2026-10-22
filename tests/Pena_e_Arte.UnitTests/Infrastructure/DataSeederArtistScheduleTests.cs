using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence.Seed;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Infrastructure;

/// <summary>Demo artists had no weekly schedule, so a fresh local database couldn't be booked (2026-10-22).</summary>
public class DataSeederArtistScheduleTests
{
    private static readonly Guid DemoStudio2Id = new("bbbb0002-0000-0000-0000-000000000000");
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private Artist AddArtist(Guid studioId, string first)
    {
        Artist artist = new() { StudioId = studioId, FirstName = first, LastName = "Test", Email = $"{first}@test.com" };
        _db.Artists.Add(artist);
        return artist;
    }

    [Fact]
    public async Task EnsureArtistSchedules_DemoArtistWithoutOne_GetsTheStudiosDemoWeek()
    {
        Artist artist = AddArtist(DemoStudio2Id, "luis");
        await _db.SaveChangesAsync();

        await DataSeeder.EnsureArtistSchedulesAsync(_db);

        List<ArtistSchedule> week = _db.ArtistSchedules.Where(s => s.ArtistId == artist.Id).ToList();
        week.Should().HaveCount(6);
        week.Single(s => s.DayOfWeek == DayOfWeek.Tuesday).StartTime.Should().Be(new TimeSpan(9, 0, 0));
        week.Single(s => s.DayOfWeek == DayOfWeek.Saturday).EndTime.Should().Be(new TimeSpan(16, 0, 0));
        week.Should().OnlyContain(s => s.IsAvailable && s.StudioId == DemoStudio2Id);
        week.Should().NotContain(s => s.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public async Task EnsureArtistSchedules_ArtistWithASchedule_IsLeftAlone_AndOtherStudiosAreUntouched()
    {
        Artist scheduled = AddArtist(DemoStudio2Id, "marta");
        Artist elsewhere = AddArtist(Guid.NewGuid(), "real");
        _db.ArtistSchedules.Add(new ArtistSchedule
        {
            StudioId = DemoStudio2Id,
            ArtistId = scheduled.Id,
            DayOfWeek = DayOfWeek.Sunday,
            StartTime = new TimeSpan(12, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
        });
        await _db.SaveChangesAsync();

        await DataSeeder.EnsureArtistSchedulesAsync(_db);
        await DataSeeder.EnsureArtistSchedulesAsync(_db); // idempotent

        _db.ArtistSchedules.Where(s => s.ArtistId == scheduled.Id).Should().ContainSingle();
        _db.ArtistSchedules.Where(s => s.ArtistId == elsewhere.Id).Should().BeEmpty();
    }
}
