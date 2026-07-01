using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class UpsertArtistScheduleHandlerTests
{
    private readonly FakeDbContext  _db     = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public UpsertArtistScheduleHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private UpsertArtistScheduleHandler CreateSut() => new(_db, _tenant);

    private Guid SeedArtist()
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId  = _studioId,
            FirstName = "Test",
            LastName  = "Artist",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        return artist.Id;
    }

    [Fact]
    public async Task Handle_NewEntries_InsertsScheduleRows()
    {
        Guid artistId = SeedArtist();
        List<ScheduleEntryDto> entries =
        [
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true),
            new(DayOfWeek.Friday, TimeSpan.FromHours(10), TimeSpan.FromHours(18), true),
        ];

        await CreateSut().Handle(new UpsertArtistScheduleCommand(artistId, entries), default);

        _db.ArtistSchedules.Should().HaveCount(2);
        _db.ArtistSchedules.Should().ContainSingle(s => s.DayOfWeek == DayOfWeek.Monday);
        _db.ArtistSchedules.Should().ContainSingle(s => s.DayOfWeek == DayOfWeek.Friday);
    }

    [Fact]
    public async Task Handle_ExistingEntry_UpdatesInPlace()
    {
        Guid artistId = SeedArtist();
        _db.ArtistSchedules.Add(new Domain.Entities.ArtistSchedule
        {
            ArtistId    = artistId,
            StudioId    = _studioId,
            DayOfWeek   = DayOfWeek.Tuesday,
            StartTime   = TimeSpan.FromHours(8),
            EndTime     = TimeSpan.FromHours(16),
            IsAvailable = true,
        });
        _db.SaveChanges();

        List<ScheduleEntryDto> updated =
        [
            new(DayOfWeek.Tuesday, TimeSpan.FromHours(10), TimeSpan.FromHours(20), false),
        ];

        await CreateSut().Handle(new UpsertArtistScheduleCommand(artistId, updated), default);

        _db.ArtistSchedules.Should().HaveCount(1);
        Domain.Entities.ArtistSchedule row = _db.ArtistSchedules.First();
        row.StartTime.Should().Be(TimeSpan.FromHours(10));
        row.EndTime.Should().Be(TimeSpan.FromHours(20));
        row.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnknownArtist_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new UpsertArtistScheduleCommand(Guid.NewGuid(), []), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_MixedNewAndExisting_UpsertsBoth()
    {
        Guid artistId = SeedArtist();
        _db.ArtistSchedules.Add(new Domain.Entities.ArtistSchedule
        {
            ArtistId    = artistId,
            StudioId    = _studioId,
            DayOfWeek   = DayOfWeek.Wednesday,
            StartTime   = TimeSpan.FromHours(9),
            EndTime     = TimeSpan.FromHours(17),
            IsAvailable = true,
        });
        _db.SaveChanges();

        List<ScheduleEntryDto> entries =
        [
            new(DayOfWeek.Wednesday, TimeSpan.FromHours(11), TimeSpan.FromHours(19), true),
            new(DayOfWeek.Thursday,  TimeSpan.FromHours(9),  TimeSpan.FromHours(17), true),
        ];

        await CreateSut().Handle(new UpsertArtistScheduleCommand(artistId, entries), default);

        _db.ArtistSchedules.Should().HaveCount(2);
        _db.ArtistSchedules.First(s => s.DayOfWeek == DayOfWeek.Wednesday).StartTime
           .Should().Be(TimeSpan.FromHours(11));
    }
}
