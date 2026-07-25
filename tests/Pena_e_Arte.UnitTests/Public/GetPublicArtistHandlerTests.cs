using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPublicArtistHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicArtistHandler CreateSut() => new(_db);

    private async Task<(Artist artist, Studio studio)> SeedArtistWithStudioAsync(
        Guid? userId = null,
        string? profileImageUrl = null,
        string? specializations = null,
        decimal? hourlyRate = null)
    {
        Guid studioId = Guid.NewGuid();
        Studio studio = new()
        {
            Id       = studioId,
            Name     = "Ink Studio",
            Slug     = "ink-studio",
            City     = "Lisbon",
            IsActive = true,
        };
        _db.Studios.Add(studio);

        Artist artist = new()
        {
            StudioId        = studioId,
            UserId          = userId,
            FirstName       = "Maria",
            LastName        = "Silva",
            Email           = "maria@example.com",
            ProfileImageUrl = profileImageUrl,
            Specializations = specializations,
            HourlyRate      = hourlyRate,
        };
        artist.SetSlug("maria-silva");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img1.jpg" });
        _db.PortfolioImages.Add(new PortfolioImage { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "img2.jpg" });
        await _db.SaveChangesAsync();

        return (artist, studio);
    }

    [Fact]
    public async Task Returns_null_when_slug_does_not_exist()
    {
        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("no-such-slug", null), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_studio_is_inactive()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Closed Studio", Slug = "closed-studio", City = "Porto", IsActive = false });

        Artist artist = new() { StudioId = studioId, FirstName = "Ana", LastName = "Lima", Email = "ana@example.com" };
        artist.SetSlug("ana-lima");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("ana-lima", null), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_response_with_correct_name_and_slug()
    {
        await SeedArtistWithStudioAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Maria Silva");
        result.Slug.Should().Be("maria-silva");
    }

    [Fact]
    public async Task IsOwnProfile_true_when_currentUserId_matches_artist_userId()
    {
        Guid userId = Guid.NewGuid();
        await SeedArtistWithStudioAsync(userId: userId);

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", userId), CancellationToken.None);

        result!.IsOwnProfile.Should().BeTrue();
    }

    [Fact]
    public async Task IsOwnProfile_false_when_currentUserId_is_null()
    {
        await SeedArtistWithStudioAsync(userId: Guid.NewGuid());

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.IsOwnProfile.Should().BeFalse();
    }

    [Fact]
    public async Task IsOwnProfile_false_when_currentUserId_does_not_match()
    {
        await SeedArtistWithStudioAsync(userId: Guid.NewGuid());

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", Guid.NewGuid()), CancellationToken.None);

        result!.IsOwnProfile.Should().BeFalse();
    }

    [Fact]
    public async Task AverageRating_is_null_when_no_reviews()
    {
        await SeedArtistWithStudioAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.AverageRating.Should().BeNull();
        result.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task AverageRating_and_ReviewCount_are_computed_from_reviews()
    {
        (Artist artist, _) = await SeedArtistWithStudioAsync();
        Guid authorId = Guid.NewGuid();

        _db.Reviews.Add(Review.ForArtist(artist.Id, Guid.NewGuid(), authorId, "Client A", 5, "Great work!"));
        _db.Reviews.Add(Review.ForArtist(artist.Id, Guid.NewGuid(), Guid.NewGuid(), "Client B", 3, "Good session."));
        await _db.SaveChangesAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.ReviewCount.Should().Be(2);
        result.AverageRating.Should().Be(4.0);
    }

    [Fact]
    public async Task ProfileImageUrl_is_projected_correctly()
    {
        await SeedArtistWithStudioAsync(profileImageUrl: "https://cdn.example.com/avatar.jpg");

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.ProfileImageUrl.Should().Be("https://cdn.example.com/avatar.jpg");
    }

    [Fact]
    public async Task ProfileImageUrl_is_null_when_not_set()
    {
        await SeedArtistWithStudioAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.ProfileImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Specializations_are_projected_correctly()
    {
        await SeedArtistWithStudioAsync(specializations: "Blackwork, Mandala");

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.Specializations.Should().Be("Blackwork, Mandala");
    }

    [Fact]
    public async Task HourlyRate_is_projected_correctly()
    {
        await SeedArtistWithStudioAsync(hourlyRate: 150m);

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.HourlyRate.Should().Be(150m);
    }

    [Fact]
    public async Task HourlyRate_is_null_when_not_set()
    {
        await SeedArtistWithStudioAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.HourlyRate.Should().BeNull();
    }

    [Fact]
    public async Task PortfolioImages_include_style()
    {
        (Artist artist, _) = await SeedArtistWithStudioAsync();
        PortfolioImage styled = _db.PortfolioImages.Local
            .First(p => p.ArtistId == artist.Id && p.ImageUrl == "img1.jpg");
        styled.Style = "blackwork";
        await _db.SaveChangesAsync();

        PublicArtistResponse? result = await CreateSut().Handle(
            new GetPublicArtistQuery("maria-silva", null), CancellationToken.None);

        result!.PortfolioImages.Should().Contain(p => p.ImageUrl == "img1.jpg" && p.Style == "blackwork");
        result.PortfolioImages.Should().Contain(p => p.ImageUrl == "img2.jpg" && p.Style == null);
    }
}
