using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPublicStudioHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicStudioHandler CreateSut() => new(_db);

    private static Studio MakeStudio(string slug = "test-studio", bool active = true) => new()
    {
        Name            = "Test Studio",
        Slug            = slug,
        City            = "Porto",
        IsActive        = active,
        PhoneNumber     = "+351 912 000 000",
        InstagramHandle = "teststudio",
    };

    private static Artist MakeArtist(Guid studioId, string slug)
    {
        Artist artist = new()
        {
            StudioId        = studioId,
            FirstName       = "Ana",
            LastName        = "Sousa",
            Email           = $"{slug}@test.com",
            Specializations = "Blackwork, Mandala",
        };
        artist.SetSlug(slug);
        return artist;
    }

    private async Task AddPortfolioImages(Artist artist, List<string> urls)
    {
        foreach (string url in urls)
            _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = artist.StudioId, ImageUrl = url });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_UnknownSlug_ReturnsNull()
    {
        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("no-such-studio"), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_InactiveStudio_ReturnsNull()
    {
        _db.Studios.Add(MakeStudio(active: false));
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ActiveStudio_ReturnsStudioWithContactFields()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result.Should().NotBeNull();
        result!.PhoneNumber.Should().Be("+351 912 000 000");
        result.InstagramHandle.Should().Be("teststudio");
    }

    [Fact]
    public async Task Handle_StudioWithNoReviews_ReturnsNullAverageRatingAndZeroCount()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.AverageRating.Should().BeNull();
        result.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_StudioWithReviews_ReturnsCorrectAggregates()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _db.Reviews.Add(Review.ForStudio(studio.Id, Guid.NewGuid(), "Alice", 5, "Great!"));
        _db.Reviews.Add(Review.ForStudio(studio.Id, Guid.NewGuid(), "Bob",   3, "OK."));
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.AverageRating.Should().Be(4.0);
        result.ReviewCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ArtistWithReviews_PerArtistAggregatesAreCorrect()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        Artist artist = MakeArtist(studio.Id, "ana-sousa");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        _db.Reviews.Add(Review.ForArtist(artist.Id, Guid.NewGuid(), "C", 4, "Nice."));
        _db.Reviews.Add(Review.ForArtist(artist.Id, Guid.NewGuid(), "D", 2, "Meh."));
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        PublicArtistSummary? summary = result!.Artists.FirstOrDefault(a => a.Slug == "ana-sousa");
        summary.Should().NotBeNull();
        summary!.AverageRating.Should().Be(3.0);
        summary.ReviewCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ArtistWithNoReviews_AverageRatingIsNullAndCountIsZero()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _db.Artists.Add(MakeArtist(studio.Id, "no-reviews-artist"));
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        PublicArtistSummary? summary = result!.Artists.Single();
        summary.AverageRating.Should().BeNull();
        summary.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_GalleryImages_MaxNineRoundRobin()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        // Artist A: 3 images; Artist B: 3 images; Artist C: 3 images → 9 total
        Artist artistA = MakeArtist(studio.Id, "artist-a");
        Artist artistB = MakeArtist(studio.Id, "artist-b");
        Artist artistC = MakeArtist(studio.Id, "artist-c");
        _db.Artists.AddRange(artistA, artistB, artistC);
        await _db.SaveChangesAsync();
        await AddPortfolioImages(artistA, ["a1", "a2", "a3"]);
        await AddPortfolioImages(artistB, ["b1", "b2", "b3"]);
        await AddPortfolioImages(artistC, ["c1", "c2", "c3"]);

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.GalleryImages.Should().HaveCount(9);
        // Round-robin: first pass picks index 0 from each artist
        result.GalleryImages.Should().Contain("a1").And.Contain("b1").And.Contain("c1");
    }

    [Fact]
    public async Task Handle_GalleryImages_CappedAtNineEvenWithMoreAvailable()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        // 4 artists × 3 images = 12 available; gallery should cap at 9
        for (int i = 0; i < 4; i++)
        {
            Artist a = MakeArtist(studio.Id, $"artist-{i}");
            _db.Artists.Add(a);
            await _db.SaveChangesAsync();
            await AddPortfolioImages(a, [$"img-{i}-1", $"img-{i}-2", $"img-{i}-3"]);
        }

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.GalleryImages.Should().HaveCount(9);
    }

    [Fact]
    public async Task Handle_NoArtistsHavePortfolioImages_GalleryIsEmpty()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _db.Artists.Add(MakeArtist(studio.Id, "artist-no-images"));
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.GalleryImages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NormalArtists_AllReturnedWithDistinctIds()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _db.Artists.Add(MakeArtist(studio.Id, "artist-x"));
        _db.Artists.Add(MakeArtist(studio.Id, "artist-y"));
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.Artists.Should().HaveCount(2);
        result.Artists.Select(a => a.ArtistId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_DeletedArtists_NotIncludedInResponse()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        Artist active  = MakeArtist(studio.Id, "active-artist");
        Artist deleted = MakeArtist(studio.Id, "deleted-artist");
        deleted.DeletedAt = DateTime.UtcNow;

        _db.Artists.Add(active);
        _db.Artists.Add(deleted);
        await _db.SaveChangesAsync();

        PublicStudioResponse? result =
            await CreateSut().Handle(new GetPublicStudioQuery("test-studio"), default);

        result!.Artists.Should().ContainSingle(a => a.Slug == "active-artist");
        result.Artists.Should().NotContain(a => a.Slug == "deleted-artist");
    }
}

