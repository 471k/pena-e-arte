using FluentAssertions;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class DeleteArtistTimeOffHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid            _studioId    = Guid.NewGuid();

    private DeleteArtistTimeOffHandler CreateSut() => new(_db, _currentUser);

    private (Guid ArtistId, Guid TimeOffId) SeedTimeOff(Guid? userId = null)
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId  = _studioId,
            UserId    = userId,
            FirstName = "T",
            LastName  = "O",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);

        var timeOff = new Domain.Entities.ArtistTimeOff
        {
            ArtistId  = artist.Id,
            StudioId  = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            EndDate   = DateTime.UtcNow.Date.AddDays(5),
            Reason    = "Holiday",
        };
        _db.ArtistTimeOffs.Add(timeOff);
        _db.SaveChanges();
        return (artist.Id, timeOff.Id);
    }

    [Fact]
    public async Task Handle_ValidCommand_SoftDeletesTimeOff()
    {
        (Guid artistId, Guid timeOffId) = SeedTimeOff();

        await CreateSut().Handle(new DeleteArtistTimeOffCommand(artistId, timeOffId), default);

        Domain.Entities.ArtistTimeOff row = _db.ArtistTimeOffs.First();
        row.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownTimeOff_ThrowsNotFoundException()
    {
        (Guid artistId, _) = SeedTimeOff();

        Func<Task> act = () => CreateSut().Handle(
            new DeleteArtistTimeOffCommand(artistId, Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_TimeOffFromDifferentArtist_ThrowsNotFoundException()
    {
        (_, Guid timeOffId) = SeedTimeOff();

        Func<Task> act = () => CreateSut().Handle(
            new DeleteArtistTimeOffCommand(Guid.NewGuid(), timeOffId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistDeletingOwnTimeOff_Succeeds()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        (Guid artistId, Guid timeOffId) = SeedTimeOff(artistUser.UserId);
        DeleteArtistTimeOffHandler sut = new(_db, artistUser);

        Func<Task> act = () => sut.Handle(new DeleteArtistTimeOffCommand(artistId, timeOffId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ArtistDeletingAnotherArtistsTimeOff_ThrowsForbidden()
    {
        (Guid artistId, Guid timeOffId) = SeedTimeOff(Guid.NewGuid());
        FakeCurrentUser otherArtistUser = FakeCurrentUser.Artist();
        DeleteArtistTimeOffHandler sut = new(_db, otherArtistUser);

        Func<Task> act = () => sut.Handle(new DeleteArtistTimeOffCommand(artistId, timeOffId), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
