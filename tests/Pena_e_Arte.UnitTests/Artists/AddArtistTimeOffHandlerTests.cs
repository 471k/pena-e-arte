using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class AddArtistTimeOffHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public AddArtistTimeOffHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private AddArtistTimeOffHandler CreateSut() => new(_db, _tenant);

    private Guid SeedArtist()
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId  = _studioId,
            FirstName = "Art",
            LastName  = "Ist",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        return artist.Id;
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsNewId()
    {
        Guid artistId = SeedArtist();
        DateTime start = DateTime.UtcNow.Date.AddDays(5);
        DateTime end   = DateTime.UtcNow.Date.AddDays(10);

        Guid id = await CreateSut().Handle(
            new AddArtistTimeOffCommand(artistId, start, end, "Holiday"), default);

        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsTimeOff()
    {
        Guid artistId = SeedArtist();
        DateTime start = DateTime.UtcNow.Date.AddDays(1);
        DateTime end   = DateTime.UtcNow.Date.AddDays(3);

        await CreateSut().Handle(
            new AddArtistTimeOffCommand(artistId, start, end, "Sick leave"), default);

        _db.ArtistTimeOffs.Should().ContainSingle(t => t.ArtistId == artistId && t.Reason == "Sick leave");
    }

    [Fact]
    public async Task Handle_UnknownArtist_ThrowsNotFoundException()
    {
        DateTime start = DateTime.UtcNow.Date;
        DateTime end   = start.AddDays(2);

        Func<Task> act = () => CreateSut().Handle(
            new AddArtistTimeOffCommand(Guid.NewGuid(), start, end, "Holiday"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StoredDatesToDatePart()
    {
        Guid artistId = SeedArtist();
        DateTime start = new(2026, 7, 1, 14, 30, 0, DateTimeKind.Utc);
        DateTime end   = new(2026, 7, 5, 23, 59, 0, DateTimeKind.Utc);

        await CreateSut().Handle(
            new AddArtistTimeOffCommand(artistId, start, end, "Vacation"), default);

        Domain.Entities.ArtistTimeOff row = _db.ArtistTimeOffs.First();
        row.StartDate.Should().Be(start.Date);
        row.EndDate.Should().Be(end.Date);
    }
}
