using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class CheckPublicSlotAvailabilityHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private CheckPublicSlotAvailabilityHandler CreateSut() => new(_db);

    private static Studio MakeStudio(string slug = "guest-studio") => new()
    {
        Name = "Guest Studio", Slug = slug, City = "Porto", IsActive = true, IsPublished = true,
    };

    private static DateTime NextMondayAt(int hour)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date.AddHours(hour);
    }

    [Fact]
    public async Task Handle_AnyArtist_NoActiveArtists_ReturnsUnavailable()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckPublicSlotAvailabilityQuery(studio.Slug, null, NextMondayAt(10), 60), default);

        result.Available.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SpecificArtist_NoScheduleEntry_ReturnsUnavailableWithReason()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        Artist artist = new()
        {
            StudioId = studio.Id, FirstName = "Luna", LastName = "Artista",
            Email = "luna@test.com", IsActive = true,
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckPublicSlotAvailabilityQuery(studio.Slug, artist.Id, NextMondayAt(10), 60), default);

        result.Available.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_SpecificArtist_WithinOpenSchedule_ReturnsAvailable()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        Artist artist = new()
        {
            StudioId = studio.Id, FirstName = "Luna", LastName = "Artista",
            Email = "luna@test.com", IsActive = true,
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        _db.ArtistSchedules.Add(new ArtistSchedule
        {
            StudioId = studio.Id, ArtistId = artist.Id, DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromHours(23),
            IsAvailable = true,
        });
        await _db.SaveChangesAsync();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckPublicSlotAvailabilityQuery(studio.Slug, artist.Id, NextMondayAt(10), 60), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new CheckPublicSlotAvailabilityQuery("no-such-slug", null, NextMondayAt(10), 60), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
