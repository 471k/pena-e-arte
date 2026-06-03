using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class DeleteArtistHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private DeleteArtistHandler CreateSut() => new(_db);

    private async Task<Artist> SeedArtist(string email)
    {
        Artist artist = new() { StudioId = _studioId, FirstName = "A", LastName = "B", Email = email };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    [Fact]
    public async Task Handle_ExistingArtist_SetsDeletedAt()
    {
        Artist artist = await SeedArtist("rui@studio.com");

        await CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        _db.Artists.IgnoreQueryFilters()
            .Single(a => a.Id == artist.Id)
            .DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ExistingArtist_DoesNotHardDelete()
    {
        Artist artist = await SeedArtist("rui@studio.com");

        await CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        _db.Artists.IgnoreQueryFilters().Should().ContainSingle(a => a.Id == artist.Id);
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeleteArtistCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
