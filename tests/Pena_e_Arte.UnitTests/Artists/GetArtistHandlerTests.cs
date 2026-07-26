using FluentAssertions;
using Pena_e_Arte.Application.Artists.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class GetArtistHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetArtistHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingArtist_ReturnsArtistResponse()
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Rui",
            LastName = "Tavares",
            Email = "rui@studio.com",
            Specializations = "Realism"
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        ArtistResponse result = await CreateSut().Handle(new GetArtistQuery(artist.Id), default);

        result.Id.Should().Be(artist.Id);
        result.FirstName.Should().Be("Rui");
        result.LastName.Should().Be("Tavares");
        result.Email.Should().Be("rui@studio.com");
        result.Specializations.Should().Be("Realism");
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetArtistQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
