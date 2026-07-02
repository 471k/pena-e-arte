using FluentAssertions;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class UpdateArtistHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid            _studioId    = Guid.NewGuid();

    private UpdateArtistHandler CreateSut() => new(_db, _currentUser);

    private async Task<Artist> SeedArtist(string firstName, string lastName, string email, string? specializations = null, Guid? userId = null)
    {
        Artist artist = new()
        {
            StudioId        = _studioId,
            UserId          = userId,
            FirstName       = firstName,
            LastName        = lastName,
            Email           = email,
            Specializations = specializations
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    [Fact]
    public async Task Handle_ExistingArtist_ReturnsUpdatedResponse()
    {
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com");
        UpdateArtistRequest req = new("Ricardo", "Tavares", "ricardo@studio.com", "Realism");

        ArtistResponse result = await CreateSut().Handle(new UpdateArtistCommand(artist.Id, req), default);

        result.FirstName.Should().Be("Ricardo");
        result.LastName.Should().Be("Tavares");
        result.Email.Should().Be("ricardo@studio.com");
        result.Specializations.Should().Be("Realism");
    }

    [Fact]
    public async Task Handle_ExistingArtist_PersistsChanges()
    {
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com");
        UpdateArtistRequest req = new("Ricardo", "Ferreira", "ricardo@studio.com", null);

        await CreateSut().Handle(new UpdateArtistCommand(artist.Id, req), default);

        Artist updated = _db.Artists.Single(a => a.Id == artist.Id);
        updated.FirstName.Should().Be("Ricardo");
        updated.LastName.Should().Be("Ferreira");
        updated.Email.Should().Be("ricardo@studio.com");
    }

    [Fact]
    public async Task Handle_SameEmail_DoesNotThrow()
    {
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com");
        UpdateArtistRequest req = new("Rui", "Tavares", "rui@studio.com", "Realism");

        Func<Task> act = () => CreateSut().Handle(new UpdateArtistCommand(artist.Id, req), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmailTakenByOtherArtist_ThrowsBusinessRuleViolationException()
    {
        await SeedArtist("Ana", "Lima", "ana@studio.com");
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com");
        UpdateArtistRequest req = new("Rui", "Tavares", "ana@studio.com", null);

        Func<Task> act = () => CreateSut().Handle(new UpdateArtistCommand(artist.Id, req), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*ana@studio.com*");
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        UpdateArtistRequest req = new("X", "Y", "x@studio.com", null);

        Func<Task> act = () => CreateSut().Handle(new UpdateArtistCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_Update_SetsUpdatedAt()
    {
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com");
        DateTime before = artist.UpdatedAt;

        await Task.Delay(10);
        UpdateArtistRequest req = new("Rui", "Tavares", "rui@studio.com", "Neo-trad");
        await CreateSut().Handle(new UpdateArtistCommand(artist.Id, req), default);

        _db.Artists.Single(a => a.Id == artist.Id).UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task Handle_ArtistEditingOwnProfile_Succeeds()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com", userId: artistUser.UserId);
        UpdateArtistHandler sut = new(_db, artistUser);
        UpdateArtistRequest req = new("Rui", "Tavares", "rui@studio.com", "Realism");

        Func<Task> act = () => sut.Handle(new UpdateArtistCommand(artist.Id, req), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ArtistEditingAnotherArtistsProfile_ThrowsForbidden()
    {
        Artist artist = await SeedArtist("Rui", "Tavares", "rui@studio.com", userId: Guid.NewGuid());
        FakeCurrentUser otherArtistUser = FakeCurrentUser.Artist();
        UpdateArtistHandler sut = new(_db, otherArtistUser);
        UpdateArtistRequest req = new("Hacked", "Tavares", "rui@studio.com", null);

        Func<Task> act = () => sut.Handle(new UpdateArtistCommand(artist.Id, req), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
