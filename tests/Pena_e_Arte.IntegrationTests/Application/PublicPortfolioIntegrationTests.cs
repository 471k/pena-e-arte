using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PublicPortfolioIntegrationTests(DatabaseFixture fixture)
{
    // ── GetPublicStudio ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicStudio_ActiveStudio_ReturnsResponse()
    {
        Studio studio = await SeedStudio(isActive: true);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicStudioHandler handler = new(db);
        PublicStudioResponse? result = await handler.Handle(new GetPublicStudioQuery(studio.Slug), default);

        result.Should().NotBeNull();
        result!.StudioId.Should().Be(studio.Id);
        result.Name.Should().Be(studio.Name);
        result.Slug.Should().Be(studio.Slug);
    }

    [Fact]
    public async Task GetPublicStudio_InactiveStudio_ReturnsNull()
    {
        Studio studio = await SeedStudio(isActive: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicStudioHandler handler = new(db);
        PublicStudioResponse? result = await handler.Handle(new GetPublicStudioQuery(studio.Slug), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublicStudio_UnknownSlug_ReturnsNull()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicStudioHandler handler = new(db);
        PublicStudioResponse? result = await handler.Handle(new GetPublicStudioQuery("does-not-exist-xyz"), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublicStudio_WithArtists_IncludesArtistList()
    {
        Studio studio = await SeedStudio(isActive: true);
        await SeedArtist(studio.Id, "ink-master");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicStudioHandler handler = new(db);
        PublicStudioResponse? result = await handler.Handle(new GetPublicStudioQuery(studio.Slug), default);

        result!.Artists.Should().HaveCount(1);
        result.Artists[0].Slug.Should().Be("ink-master");
    }

    [Fact]
    public async Task GetPublicStudio_DeletedArtistExcluded()
    {
        Studio studio  = await SeedStudio(isActive: true);
        Artist artist  = await SeedArtist(studio.Id, "deleted-artist");

        await using AppDbContext deleteSeed = fixture.CreateDbContext(studio.Id);
        Artist? a = await deleteSeed.Artists.FindAsync(artist.Id);
        a!.DeletedAt = DateTime.UtcNow;
        await deleteSeed.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicStudioHandler handler = new(db);
        PublicStudioResponse? result = await handler.Handle(new GetPublicStudioQuery(studio.Slug), default);

        result!.Artists.Should().BeEmpty();
    }

    // ── GetPublicArtist ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicArtist_KnownSlug_ReturnsResponse()
    {
        Studio studio = await SeedStudio(isActive: true);
        Artist artist = await SeedArtist(studio.Id, "jane-doe");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicArtistHandler handler = new(db);
        PublicArtistResponse? result = await handler.Handle(new GetPublicArtistQuery("jane-doe", null), default);

        result.Should().NotBeNull();
        result!.ArtistId.Should().Be(artist.Id);
        result.StudioSlug.Should().Be(studio.Slug);
        result.ShowBookingCta.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicArtist_UnknownSlug_ReturnsNull()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicArtistHandler handler = new(db);
        PublicArtistResponse? result = await handler.Handle(new GetPublicArtistQuery("no-such-artist", null), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublicArtist_StudioInactive_ReturnsNull()
    {
        Studio studio = await SeedStudio(isActive: false);
        await SeedArtist(studio.Id, "orphan-artist");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicArtistHandler handler = new(db);
        PublicArtistResponse? result = await handler.Handle(new GetPublicArtistQuery("orphan-artist", null), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublicArtist_WithPortfolioImages_ReturnsThem()
    {
        Studio studio = await SeedStudio(isActive: true);
        Artist artist = await SeedArtist(studio.Id, "gallery-artist", portfolioImages: ["https://cdn.example.com/1.jpg", "https://cdn.example.com/2.jpg"]);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicArtistHandler handler = new(db);
        PublicArtistResponse? result = await handler.Handle(new GetPublicArtistQuery("gallery-artist", null), default);

        result!.PortfolioImages.Should().HaveCount(2);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<Studio> SeedStudio(bool isActive)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name     = "Portfolio Studio",
            Slug     = "portfolio-" + Guid.NewGuid().ToString("N")[..8],
            City     = "Lisboa",
            IsActive = isActive,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();
        return studio;
    }

    private async Task<Artist> SeedArtist(Guid studioId, string slug, List<string>? portfolioImages = null)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Artist artist = new()
        {
            StudioId        = studioId,
            FirstName       = "Jane",
            LastName        = "Doe",
            Email           = $"{slug}@test.com",
            PortfolioImages = portfolioImages ?? [],
        };
        artist.SetSlug(slug);
        seed.Artists.Add(artist);
        await seed.SaveChangesAsync();
        return artist;
    }
}
