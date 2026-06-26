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

    [Fact]
    public async Task Returns_images_for_artists_with_portfolio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Palace", Slug = "ink-palace", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        Artist a1 = new() { StudioId = studioId, FirstName = "Ana", LastName = "Lima", Email = "a@x.com", PortfolioImages = ["img1.jpg", "img2.jpg"] };
        a1.SetSlug("ana-lima");
        Artist a2 = new() { StudioId = studioId, FirstName = "Rui", LastName = "Costa", Email = "r@x.com", PortfolioImages = ["img3.jpg", "img4.jpg"] };
        a2.SetSlug("rui-costa");

        _db.Artists.Add(a1);
        _db.Artists.Add(a2);
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Should().NotBeEmpty();
        result.Select(r => r.ArtistSlug).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Respects_max_3_images_per_artist()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Dark Ink", Slug = "dark-ink", City = "Porto", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });

        Artist a = new() { StudioId = studioId, FirstName = "Maria", LastName = "Santos", Email = "m@x.com", PortfolioImages = ["i1.jpg", "i2.jpg", "i3.jpg", "i4.jpg", "i5.jpg"] };
        a.SetSlug("maria-santos");
        _db.Artists.Add(a);
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Where(r => r.ArtistSlug == "maria-santos").Should().HaveCount(3);
    }

    [Fact]
    public async Task Excludes_artist_whose_studio_is_inactive()
    {
        Guid activeStudioId   = Guid.NewGuid();
        Guid inactiveStudioId = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = activeStudioId,   Name = "Active Studio",   Slug = "active",   City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = true  });
        _db.Studios.Add(new Studio { Id = inactiveStudioId, Name = "Inactive Studio", Slug = "inactive", City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = false });

        Artist aActive = new() { StudioId = activeStudioId,   FirstName = "João", LastName = "Silva", Email = "j@x.com", PortfolioImages = ["img.jpg"] };
        aActive.SetSlug("joao-silva");
        Artist aInactive = new() { StudioId = inactiveStudioId, FirstName = "Pedro", LastName = "Lopes", Email = "p@x.com", PortfolioImages = ["img2.jpg"] };
        aInactive.SetSlug("pedro-lopes");

        _db.Artists.Add(aActive);
        _db.Artists.Add(aInactive);
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(null, null, 50, 1), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "joao-silva");
    }

    [Fact]
    public async Task Excludes_artist_with_no_portfolio_images()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Minimal Studio", Slug = "minimal", City = "Lisbon", Latitude = 38.7, Longitude = -9.1, IsActive = true });

        Artist aWithImages    = new() { StudioId = studioId, FirstName = "Alice", LastName = "Ferreira", Email = "af@x.com", PortfolioImages = ["img.jpg"] };
        aWithImages.SetSlug("alice-ferreira");
        Artist aWithoutImages = new() { StudioId = studioId, FirstName = "Bob",   LastName = "Rodrigues", Email = "br@x.com", PortfolioImages = [] };
        aWithoutImages.SetSlug("bob-rodrigues");

        _db.Artists.Add(aWithImages);
        _db.Artists.Add(aWithoutImages);
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
        {
            Artist a = new() { StudioId = studioId, FirstName = $"Artist{i}", LastName = "X", Email = $"a{i}@x.com", PortfolioImages = [$"img{i}.jpg"] };
            a.SetSlug($"artist-{i}");
            _db.Artists.Add(a);
        }
        await _db.SaveChangesAsync();

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
        Guid studioFarId  = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = studioNearId, Name = "Near Studio", Slug = "near", City = "Lisbon", Latitude = 38.7169, Longitude = -9.1395, IsActive = true });
        _db.Studios.Add(new Studio { Id = studioFarId,  Name = "Far Studio",  Slug = "far",  City = "Berlin", Latitude = 52.5200, Longitude = 13.4050, IsActive = true });

        Artist near = new() { StudioId = studioNearId, FirstName = "Near", LastName = "Artist", Email = "n@x.com", PortfolioImages = ["n.jpg"] };
        near.SetSlug("near-artist");
        Artist far = new() { StudioId = studioFarId, FirstName = "Far", LastName = "Artist", Email = "f@x.com", PortfolioImages = ["f.jpg"] };
        far.SetSlug("far-artist");

        _db.Artists.Add(near);
        _db.Artists.Add(far);
        await _db.SaveChangesAsync();

        List<PortfolioImageResponse> result = await CreateSut().Handle(
            new GetPortfolioFeedQuery(38.7169, -9.1395, 50, 1), CancellationToken.None);

        result.Should().OnlyContain(r => r.ArtistSlug == "near-artist");
    }
}
