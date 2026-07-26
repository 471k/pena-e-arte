using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPublicArtistInstagramPostsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicArtistInstagramPostsHandler CreateSut() => new(_db);

    private async Task<Artist> SeedArtistWithPostAsync(bool studioActive = true)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Ink Studio",
            Slug = "ink-studio",
            City = "Lisbon",
            IsActive = studioActive,
        });

        Artist artist = new()
        {
            StudioId = studioId,
            FirstName = "Maria",
            LastName = "Silva",
            Email = "maria@example.com",
        };
        artist.SetSlug("maria-silva");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        _db.InstagramPosts.Add(new InstagramPost
        {
            StudioId = studioId,
            ArtistId = artist.Id,
            InstagramMediaId = "media-1",
            MediaUrl = "https://cdn.example.com/1.jpg",
            MediaType = "IMAGE",
            PostedAt = DateTime.UtcNow,
            IsVisible = true,
        });
        await _db.SaveChangesAsync();

        return artist;
    }

    [Fact]
    public async Task Returns_empty_when_slug_does_not_exist()
    {
        List<InstagramPostResponse> result = await CreateSut().Handle(
            new GetPublicArtistInstagramPostsQuery("no-such-slug"), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_visible_posts_for_active_studio()
    {
        Artist artist = await SeedArtistWithPostAsync();

        List<InstagramPostResponse> result = await CreateSut().Handle(
            new GetPublicArtistInstagramPostsQuery(artist.Slug!), CancellationToken.None);

        result.Should().ContainSingle(p => p.InstagramMediaId == "media-1");
    }

    [Fact]
    public async Task Returns_empty_when_studio_is_suspended()
    {
        Artist artist = await SeedArtistWithPostAsync(studioActive: false);

        List<InstagramPostResponse> result = await CreateSut().Handle(
            new GetPublicArtistInstagramPostsQuery(artist.Slug!), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_hidden_posts()
    {
        Artist artist = await SeedArtistWithPostAsync();
        _db.InstagramPosts.Add(new InstagramPost
        {
            StudioId = artist.StudioId,
            ArtistId = artist.Id,
            InstagramMediaId = "media-hidden",
            MediaUrl = "https://cdn.example.com/2.jpg",
            MediaType = "IMAGE",
            PostedAt = DateTime.UtcNow,
            IsVisible = false,
        });
        await _db.SaveChangesAsync();

        List<InstagramPostResponse> result = await CreateSut().Handle(
            new GetPublicArtistInstagramPostsQuery(artist.Slug!), CancellationToken.None);

        result.Should().ContainSingle(p => p.InstagramMediaId == "media-1");
    }
}
