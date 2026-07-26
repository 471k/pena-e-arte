using FluentAssertions;
using Pena_e_Arte.Application.Artists.Queries;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class GetArtistScheduleHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetArtistScheduleHandler CreateSut() => new(_db);

    private Guid SeedArtistWithSchedule()
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId = _studioId,
            FirstName = "A",
            LastName = "B",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        _db.ArtistSchedules.AddRange(
            new Domain.Entities.ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = _studioId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = TimeSpan.FromHours(9),
                EndTime = TimeSpan.FromHours(17),
                IsAvailable = true,
            },
            new Domain.Entities.ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = _studioId,
                DayOfWeek = DayOfWeek.Sunday,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(1),
                IsAvailable = false,
            });
        _db.ArtistTimeOffs.Add(new Domain.Entities.ArtistTimeOff
        {
            ArtistId = artist.Id,
            StudioId = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            EndDate = DateTime.UtcNow.Date.AddDays(3),
            Reason = "Holiday",
        });
        _db.SaveChanges();
        return artist.Id;
    }

    [Fact]
    public async Task Handle_ExistingArtist_ReturnsScheduleEntries()
    {
        Guid artistId = SeedArtistWithSchedule();

        ArtistAvailabilityResponse result =
            await CreateSut().Handle(new GetArtistScheduleQuery(artistId), default);

        result.Schedule.Should().HaveCount(2);
        result.Schedule.Should().ContainSingle(s => s.DayOfWeek == DayOfWeek.Monday && s.IsAvailable);
        result.Schedule.Should().ContainSingle(s => s.DayOfWeek == DayOfWeek.Sunday && !s.IsAvailable);
    }

    [Fact]
    public async Task Handle_ExistingArtist_ReturnsFutureTimeOff()
    {
        Guid artistId = SeedArtistWithSchedule();

        ArtistAvailabilityResponse result =
            await CreateSut().Handle(new GetArtistScheduleQuery(artistId), default);

        result.TimeOff.Should().HaveCount(1);
        result.TimeOff[0].Reason.Should().Be("Holiday");
    }

    [Fact]
    public async Task Handle_UnknownArtist_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetArtistScheduleQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_PastTimeOff_Excluded()
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId = _studioId,
            FirstName = "P",
            LastName = "Q",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        _db.ArtistTimeOffs.Add(new Domain.Entities.ArtistTimeOff
        {
            ArtistId = artist.Id,
            StudioId = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(-10),
            EndDate = DateTime.UtcNow.Date.AddDays(-1),
            Reason = "Past",
        });
        _db.SaveChanges();

        ArtistAvailabilityResponse result =
            await CreateSut().Handle(new GetArtistScheduleQuery(artist.Id), default);

        result.TimeOff.Should().BeEmpty();
    }
}
