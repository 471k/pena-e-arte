using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetNearbyStudiosHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetNearbyStudiosHandler CreateSut() => new(_db);

    [Fact]
    public async Task Returns_studios_within_radius()
    {
        Guid id1 = Guid.NewGuid();
        Guid id2 = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = id1, Name = "Lisbon Ink", Slug = "lisbon-ink", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });
        _db.Studios.Add(new Studio { Id = id2, Name = "Berlin Ink", Slug = "berlin-ink", City = "Berlin", Latitude = 52.5200, Longitude = 13.4050, IsActive = true });
        await _db.SaveChangesAsync();

        List<NearbyStudioResponse> result = await CreateSut().Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].StudioId.Should().Be(id1);
        result[0].DistanceKm.Should().BeLessThan(1);
    }

    [Fact]
    public async Task Returns_empty_when_no_studios_in_radius()
    {
        List<NearbyStudioResponse> result = await CreateSut().Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Inactive_studios_excluded()
    {
        _db.Studios.Add(new Studio { Name = "Closed Studio", Slug = "closed", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = false });
        await _db.SaveChangesAsync();

        List<NearbyStudioResponse> result = await CreateSut().Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ArtistCount_reflects_published_artists_in_studio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Porto Ink", Slug = "porto-ink", City = "Porto", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        Artist a1 = new() { StudioId = studioId, FirstName = "Ana", LastName = "Silva", Email = "a@x.com" };
        a1.SetSlug("ana-silva");
        Artist a2 = new() { StudioId = studioId, FirstName = "Rui", LastName = "Costa", Email = "r@x.com" };
        // a2 has no slug — should not be counted

        _db.Artists.Add(a1);
        _db.Artists.Add(a2);
        await _db.SaveChangesAsync();

        List<NearbyStudioResponse> result = await CreateSut().Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].ArtistCount.Should().Be(1);
    }
}
