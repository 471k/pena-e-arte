using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetSitemapUrlsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetSitemapUrlsHandler CreateSut() => new(_db);

    private async Task<List<SitemapUrlEntry>> RunAsync() =>
        await CreateSut().Handle(new GetSitemapUrlsQuery(), default);

    private async Task<Studio> AddStudioAsync(string slug, bool isActive = true, bool isPublished = true)
    {
        Studio studio = new() { Name = slug, Slug = slug, City = "Lisboa", IsActive = isActive, IsPublished = isPublished };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio;
    }

    private async Task AddArtistAsync(Guid studioId, string? slug, bool isActive = true, bool deleted = false)
    {
        Artist artist = new()
        {
            StudioId = studioId,
            FirstName = "Elena",
            LastName = "Martins",
            Email = $"{Guid.NewGuid():N}@test.com",
            IsActive = isActive,
            DeletedAt = deleted ? DateTime.UtcNow : null,
        };
        if (slug is not null)
            artist.SetSlug(slug);
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ListsTheMarketingPagesWithoutALastModifiedDate()
    {
        List<SitemapUrlEntry> result = await RunAsync();

        foreach (string path in SitemapPaths.Marketing)
            result.Should().ContainSingle(u => u.Path == path && u.LastModified == null);
        result.Should().Contain(u => u.Path == "/pricing");
    }

    [Fact]
    public async Task Handle_WithNoStudiosOrArtists_ReturnsOnlyTheMarketingPages()
    {
        List<SitemapUrlEntry> result = await RunAsync();

        result.Select(u => u.Path).Should().BeEquivalentTo(SitemapPaths.Marketing);
    }

    [Fact]
    public async Task Handle_IncludesActivePublishedStudioBySlug()
    {
        await AddStudioAsync("ink-studio");

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().ContainSingle(u => u.Path == "/s/ink-studio" && u.LastModified != null);
    }

    [Fact]
    public async Task Handle_ExcludesInactiveStudio()
    {
        await AddStudioAsync("closed-studio", isActive: false);

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().NotContain(u => u.Path == "/s/closed-studio");
    }

    [Fact]
    public async Task Handle_ExcludesActiveButUnpublishedStudio_BecauseItsPageRendersNothing()
    {
        await AddStudioAsync("solo-unlisted", isActive: true, isPublished: false);

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().NotContain(u => u.Path == "/s/solo-unlisted");
    }

    [Fact]
    public async Task Handle_IncludesActiveArtistWithSlugOfAnActiveStudio()
    {
        Studio studio = await AddStudioAsync("ink-studio");
        await AddArtistAsync(studio.Id, "elena-martins");

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().ContainSingle(u => u.Path == "/artist/elena-martins");
    }

    [Fact]
    public async Task Handle_IncludesArtistOfAnActiveButUnpublishedStudio()
    {
        // Artists deliberately do not require IsPublished: a solo artist is bookable from their
        // own portfolio URL before their studio is listed in the directory.
        Studio studio = await AddStudioAsync("solo-unlisted", isPublished: false);
        await AddArtistAsync(studio.Id, "solo-artist");

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().ContainSingle(u => u.Path == "/artist/solo-artist");
    }

    [Fact]
    public async Task Handle_ExcludesArtistOfAnInactiveStudio()
    {
        Studio studio = await AddStudioAsync("suspended", isActive: false);
        await AddArtistAsync(studio.Id, "orphan-artist");

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().NotContain(u => u.Path == "/artist/orphan-artist");
    }

    [Fact]
    public async Task Handle_ExcludesArtistWithNoStudioAtAll()
    {
        await AddArtistAsync(Guid.NewGuid(), "no-studio");

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().NotContain(u => u.Path == "/artist/no-studio");
    }

    [Fact]
    public async Task Handle_ExcludesArtistWithoutSlug()
    {
        Studio studio = await AddStudioAsync("ink-studio");
        await AddArtistAsync(studio.Id, slug: null);

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().NotContain(u => u.Path.StartsWith("/artist/"));
    }

    [Fact]
    public async Task Handle_ExcludesInactiveAndDeletedArtists()
    {
        Studio studio = await AddStudioAsync("ink-studio");
        await AddArtistAsync(studio.Id, "inactive-artist", isActive: false);
        await AddArtistAsync(studio.Id, "deleted-artist", deleted: true);

        List<SitemapUrlEntry> result = await RunAsync();

        result.Should().NotContain(u => u.Path.StartsWith("/artist/"));
    }
}
