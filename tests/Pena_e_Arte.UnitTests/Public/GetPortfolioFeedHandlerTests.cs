using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPortfolioFeedHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPortfolioFeedHandler CreateSut(long viewCountPerArtist = 10L)
    {
        RedisValue[] redisValues = Enumerable.Repeat((RedisValue)viewCountPerArtist, 100).ToArray();
        IDatabase redisDb = Substitute.For<IDatabase>();
        redisDb.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
               .Returns(_ => Task.FromResult(redisValues));

        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(redisDb);

        return new GetPortfolioFeedHandler(_db, redis);
    }

    private async Task<Artist> SeedArtist(Guid studioId, string slug, string email, IReadOnlyList<string> imageUrls)
    {
        Artist artist = new() { StudioId = studioId, FirstName = slug, LastName = "Test", Email = email };
        artist.SetSlug(slug);
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        foreach (string url in imageUrls)
            _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = url });
        await _db.SaveChangesAsync();

        return artist;
    }

    [Fact]
    public async Task Returns_images_for_artists_with_portfolio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        await SeedArtist(studioId, "ana-lima", "a@x.com", ["img1.jpg", "img2.jpg"]);
        await SeedArtist(studioId, "rui-costa", "r@x.com", ["img3.jpg", "img4.jpg"]);

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Should().NotBeEmpty();
        result.Select(r => r.ArtistSlug).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Returns_all_images_for_artist_with_large_portfolio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Dark Ink", Slug = "dark-ink", City = "Porto", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        await SeedArtist(studioId, "maria-santos", "m@x.com", ["i1.jpg", "i2.jpg", "i3.jpg", "i4.jpg", "i5.jpg"]);

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, 10), CancellationToken.None);

        result.Where(r => r.ArtistSlug == "maria-santos").Should().HaveCount(5);
    }

    [Fact]
    public async Task Excludes_artist_whose_studio_is_inactive()
    {
        Guid activeStudioId = Guid.NewGuid();
        Guid inactiveStudioId = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = activeStudioId, Name = "Active Studio", Slug = "active", City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = true });
        _db.Studios.Add(new Studio { Id = inactiveStudioId, Name = "Inactive Studio", Slug = "inactive", City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = false });

        await SeedArtist(activeStudioId, "joao-silva", "j@x.com", ["img.jpg"]);
        await SeedArtist(inactiveStudioId, "pedro-lopes", "p@x.com", ["img2.jpg"]);

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "joao-silva");
    }

    [Fact]
    public async Task Excludes_artist_with_no_portfolio_images()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Minimal Studio", Slug = "minimal", City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = true });

        await SeedArtist(studioId, "alice-ferreira", "af@x.com", ["img.jpg"]);
        // Bob has no portfolio images
        Artist bob = new() { StudioId = studioId, FirstName = "Bob", LastName = "Rodrigues", Email = "br@x.com" };
        bob.SetSlug("bob-rodrigues");
        _db.Artists.Add(bob);
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "alice-ferreira");
    }

    [Fact]
    public async Task Pagination_skips_correctly()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Big Studio", Slug = "big-studio", City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = true });

        for (int i = 0; i < 10; i++)
            await SeedArtist(studioId, $"artist-{i}", $"a{i}@x.com", [$"img{i}.jpg"]);

        List<PortfolioImageResponse> page1 = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, 5), CancellationToken.None);

        List<PortfolioImageResponse> page2 = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 2, 5), CancellationToken.None);

        page1.Should().HaveCount(5);
        page2.Should().HaveCount(5);
        page1.Select(r => r.ImageUrl).Should().NotIntersectWith(page2.Select(r => r.ImageUrl));
    }

    [Fact]
    public async Task Returns_empty_when_no_artists_have_portfolio()
    {
        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Distance_filter_excludes_far_artists_when_location_provided()
    {
        Guid studioNearId = Guid.NewGuid();
        Guid studioFarId = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = studioNearId, Name = "Near Studio", Slug = "near", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });
        _db.Studios.Add(new Studio { Id = studioFarId, Name = "Far Studio", Slug = "far", City = "Berlin", Latitude = 52.5200, Longitude = 13.4050, IsActive = true });

        await SeedArtist(studioNearId, "near-artist", "n@x.com", ["n.jpg"]);
        await SeedArtist(studioFarId, "far-artist", "f@x.com", ["f.jpg"]);

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(38.7169, -9.1395, 50, 1), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "near-artist");
    }

    [Fact]
    public async Task Search_matches_style_substring()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        Artist artist = await SeedArtist(studioId, "ana-lima", "a@x.com", []);
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img.jpg", Style = "blackwork" });
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, Search: "black"), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "ana-lima");
    }

    [Fact]
    public async Task Search_matches_artist_name_case_insensitively()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        await SeedArtist(studioId, "ana-lima", "a@x.com", ["img.jpg"]);
        await SeedArtist(studioId, "rui-costa", "r@x.com", ["img2.jpg"]);

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, Search: "LIMA"), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "ana-lima");
    }

    [Fact]
    public async Task Search_matches_artist_specializations_when_style_does_not_match()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        Artist artist = await SeedArtist(studioId, "ana-lima", "a@x.com", []);
        artist.Specializations = "dragon motifs, oni masks";
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img.jpg", Style = "japanese" });
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, Search: "dragon"), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "ana-lima");
    }

    [Fact]
    public async Task Search_composes_with_location_filter_as_AND()
    {
        Guid studioNearId = Guid.NewGuid();
        Guid studioFarId = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = studioNearId, Name = "Near Studio", Slug = "near", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });
        _db.Studios.Add(new Studio { Id = studioFarId, Name = "Far Studio", Slug = "far", City = "Berlin", Latitude = 52.5200, Longitude = 13.4050, IsActive = true });

        Artist nearArtist = await SeedArtist(studioNearId, "near-artist", "n@x.com", []);
        Artist farArtist  = await SeedArtist(studioFarId, "far-artist", "f@x.com", []);
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = nearArtist.Id, StudioId = studioNearId, ImageUrl = "n.jpg", Style = "dragon" });
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = farArtist.Id, StudioId = studioFarId, ImageUrl = "f.jpg", Style = "dragon" });
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(38.7169, -9.1395, 50, 1, Search: "dragon"), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "near-artist");
    }

    [Fact]
    public async Task Search_composes_with_style_filter_as_AND()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        Artist artist = await SeedArtist(studioId, "ana-lima", "a@x.com", []);
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img1.jpg", Style = "japanese" });
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img2.jpg", Style = "blackwork" });
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, Style: "japanese", Search: "lima"), CancellationToken.None);

        result.Should().ContainSingle(r => r.ImageUrl == "img1.jpg");
    }

    [Fact]
    public async Task Blank_search_behaves_identically_to_no_search()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        await SeedArtist(studioId, "ana-lima", "a@x.com", ["img1.jpg"]);
        await SeedArtist(studioId, "rui-costa", "r@x.com", ["img2.jpg"]);

        List<PortfolioImageResponse> withoutSearch = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        List<PortfolioImageResponse> withBlankSearch = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1, Search: "   "), CancellationToken.None);

        withBlankSearch.Select(r => r.ArtistSlug).Should()
            .BeEquivalentTo(withoutSearch.Select(r => r.ArtistSlug));
    }
}
