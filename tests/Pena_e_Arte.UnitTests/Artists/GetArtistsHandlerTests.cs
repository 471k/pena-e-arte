using FluentAssertions;
using Pena_e_Arte.Application.Artists.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class GetArtistsHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetArtistsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoSearch_ReturnsAllArtistsOrderedByLastNameThenFirstName()
    {
        await SeedArtists(
            ("Carlos", "Silva",   "carlos@studio.com"),
            ("Ana",    "Pereira", "ana@studio.com"),
            ("Beatriz","Silva",   "beatriz@studio.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery(null), default);

        result.Should().HaveCount(3);
        result[0].LastName.Should().Be("Pereira");
        result[1].FirstName.Should().Be("Beatriz");
        result[2].FirstName.Should().Be("Carlos");
    }

    [Fact]
    public async Task Handle_SearchMatchesFirstName_ReturnsMatchingArtists()
    {
        await SeedArtists(
            ("Rui",   "Neves", "rui@studio.com"),
            ("Maria", "Neves", "maria@studio.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery("Rui"), default);

        result.Should().ContainSingle(a => a.FirstName == "Rui");
    }

    [Fact]
    public async Task Handle_SearchMatchesLastName_ReturnsMatchingArtists()
    {
        await SeedArtists(
            ("Ana",  "Ferreira", "ana@studio.com"),
            ("Rui",  "Neves",    "rui@studio.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery("Ferreira"), default);

        result.Should().ContainSingle(a => a.LastName == "Ferreira");
    }

    [Fact]
    public async Task Handle_SearchMatchesEmail_ReturnsMatchingArtists()
    {
        await SeedArtists(
            ("Ana", "Costa",  "ana@inkstudio.com"),
            ("Rui", "Gomes",  "rui@other.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery("inkstudio"), default);

        result.Should().ContainSingle(a => a.Email == "ana@inkstudio.com");
    }

    [Fact]
    public async Task Handle_SearchIsCaseInsensitive_ReturnsMatches()
    {
        await SeedArtists(("Fernanda", "Lima", "fernanda@studio.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery("FERNANDA"), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhitespaceSearch_ReturnsAllArtists()
    {
        await SeedArtists(
            ("A", "B", "a@studio.com"),
            ("C", "D", "c@studio.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery("   "), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoMatches_ReturnsEmptyList()
    {
        await SeedArtists(("Ana", "Costa", "ana@studio.com"));

        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery("zzznomatch"), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmptyList()
    {
        List<ArtistResponse> result = await CreateSut().Handle(new GetArtistsQuery(null), default);

        result.Should().BeEmpty();
    }

    private async Task SeedArtists(params (string First, string Last, string Email)[] artists)
    {
        foreach ((string first, string last, string email) in artists)
            _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = first, LastName = last, Email = email });

        await _db.SaveChangesAsync();
    }
}
