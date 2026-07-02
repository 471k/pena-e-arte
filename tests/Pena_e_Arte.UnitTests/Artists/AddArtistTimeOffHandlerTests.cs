using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class AddArtistTimeOffHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly ICurrentTenant  _tenant      = Substitute.For<ICurrentTenant>();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid            _studioId    = Guid.NewGuid();

    public AddArtistTimeOffHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private AddArtistTimeOffHandler CreateSut() => new(_db, _tenant, _currentUser);

    private Guid SeedArtist(Guid? userId = null)
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId  = _studioId,
            UserId    = userId,
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

    [Fact]
    public async Task Handle_ArtistAddingOwnTimeOff_Succeeds()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid artistId = SeedArtist(artistUser.UserId);
        AddArtistTimeOffHandler sut = new(_db, _tenant, artistUser);
        DateTime start = DateTime.UtcNow.Date.AddDays(5);
        DateTime end   = DateTime.UtcNow.Date.AddDays(10);

        Func<Task> act = () => sut.Handle(new AddArtistTimeOffCommand(artistId, start, end, "Holiday"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ArtistAddingAnotherArtistsTimeOff_ThrowsForbidden()
    {
        Guid artistId = SeedArtist(Guid.NewGuid());
        FakeCurrentUser otherArtistUser = FakeCurrentUser.Artist();
        AddArtistTimeOffHandler sut = new(_db, _tenant, otherArtistUser);
        DateTime start = DateTime.UtcNow.Date.AddDays(5);
        DateTime end   = DateTime.UtcNow.Date.AddDays(10);

        Func<Task> act = () => sut.Handle(new AddArtistTimeOffCommand(artistId, start, end, "Holiday"), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_OverlappingExistingTimeOff_ThrowsBusinessRuleViolationException()
    {
        Guid artistId = SeedArtist();
        _db.ArtistTimeOffs.Add(new ArtistTimeOff
        {
            ArtistId  = artistId,
            StudioId  = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(5),
            EndDate   = DateTime.UtcNow.Date.AddDays(10),
            Reason    = "Existing",
        });
        await _db.SaveChangesAsync();

        DateTime overlapStart = DateTime.UtcNow.Date.AddDays(8);
        DateTime overlapEnd   = DateTime.UtcNow.Date.AddDays(12);

        Func<Task> act = () => CreateSut().Handle(
            new AddArtistTimeOffCommand(artistId, overlapStart, overlapEnd, "New request"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NonOverlappingTimeOff_Succeeds()
    {
        Guid artistId = SeedArtist();
        _db.ArtistTimeOffs.Add(new ArtistTimeOff
        {
            ArtistId  = artistId,
            StudioId  = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(5),
            EndDate   = DateTime.UtcNow.Date.AddDays(10),
            Reason    = "Existing",
        });
        await _db.SaveChangesAsync();

        DateTime start = DateTime.UtcNow.Date.AddDays(20);
        DateTime end   = DateTime.UtcNow.Date.AddDays(22);

        Func<Task> act = () => CreateSut().Handle(
            new AddArtistTimeOffCommand(artistId, start, end, "New request"), default);

        await act.Should().NotThrowAsync();
    }
}
