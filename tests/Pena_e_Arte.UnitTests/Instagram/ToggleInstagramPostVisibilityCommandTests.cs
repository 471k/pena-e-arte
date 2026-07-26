using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Instagram;

public class ToggleInstagramPostVisibilityCommandTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    private ToggleInstagramPostVisibilityHandler CreateSut() => new(_db, _currentUser);

    private async Task<(Guid ArtistId, Guid PostId)> SeedArtistWithPost(Guid? userId = null)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Rui",
            LastName = "Tavares",
            Email = "rui@studio.com",
        };
        _db.Artists.Add(artist);

        InstagramPost post = new()
        {
            StudioId = _studioId,
            ArtistId = artist.Id,
            InstagramMediaId = "media-1",
            MediaUrl = "https://example.com/1.jpg",
            MediaType = "IMAGE",
            PostedAt = DateTime.UtcNow,
            IsVisible = true,
        };
        _db.InstagramPosts.Add(post);

        await _db.SaveChangesAsync();
        return (artist.Id, post.Id);
    }

    [Fact]
    public async Task Handle_OwnerRole_CanToggleAnyArtistsPost()
    {
        (Guid artistId, Guid postId) = await SeedArtistWithPost(userId: Guid.NewGuid());
        _currentUser.Role.Returns("owner");
        _currentUser.UserId.Returns(Guid.NewGuid());

        await CreateSut().Handle(
            new ToggleInstagramPostVisibilityCommand(artistId, postId, false), default);

        _db.InstagramPosts.Single(p => p.Id == postId).IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ArtistTogglesOwnPost_Succeeds()
    {
        Guid userId = Guid.NewGuid();
        (Guid artistId, Guid postId) = await SeedArtistWithPost(userId: userId);
        _currentUser.Role.Returns("artist");
        _currentUser.UserId.Returns(userId);

        await CreateSut().Handle(
            new ToggleInstagramPostVisibilityCommand(artistId, postId, false), default);

        _db.InstagramPosts.Single(p => p.Id == postId).IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ArtistTogglesColleaguesPost_ThrowsForbidden()
    {
        (Guid artistId, Guid postId) = await SeedArtistWithPost(userId: Guid.NewGuid());
        _currentUser.Role.Returns("artist");
        _currentUser.UserId.Returns(Guid.NewGuid()); // different user — not this artist's own profile

        Func<Task> act = () => CreateSut().Handle(
            new ToggleInstagramPostVisibilityCommand(artistId, postId, false), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        _db.InstagramPosts.Single(p => p.Id == postId).IsVisible.Should().BeTrue();
    }
}
