using FluentAssertions;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class DeleteArtistTimeOffHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private DeleteArtistTimeOffHandler CreateSut() => new(_db);

    private (Guid ArtistId, Guid TimeOffId) SeedTimeOff()
    {
        var artist = new Domain.Entities.Artist
        {
            StudioId  = _studioId,
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
}
