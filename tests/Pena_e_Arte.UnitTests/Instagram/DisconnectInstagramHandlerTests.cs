using FluentAssertions;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Instagram;

public class DisconnectInstagramHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Artist _artist;
    private readonly Artist _otherArtist;

    public DisconnectInstagramHandlerTests()
    {
        _artist = NewArtist("Ana");
        _otherArtist = NewArtist("Rui");
        _db.Artists.AddRange(_artist, _otherArtist);

        foreach (Artist artist in new[] { _artist, _otherArtist })
        {
            _db.InstagramConnections.Add(new InstagramConnection
            {
                StudioId = _studioId,
                ArtistId = artist.Id,
                InstagramUserId = "ig-" + artist.FirstName,
                Username = artist.FirstName.ToLowerInvariant(),
                EncryptedToken = "encrypted-token",
                TokenExpiresAt = DateTime.UtcNow.AddDays(60),
                IsActive = true,
            });
            _db.InstagramPosts.Add(new InstagramPost
            {
                StudioId = _studioId,
                ArtistId = artist.Id,
                InstagramMediaId = "media-" + artist.FirstName,
                MediaUrl = "https://cdn.test/a.jpg",
                PostedAt = DateTime.UtcNow,
                IsVisible = true,
            });
            _db.SocialAccountLinks.Add(new SocialAccountLink
            {
                StudioId = _studioId,
                SubjectType = SocialLinkSubjectType.Artist,
                SubjectId = artist.Id,
                Platform = SocialPlatform.Instagram,
                Handle = artist.FirstName.ToLowerInvariant(),
                IsVerified = true,
                EncryptedToken = "encrypted-token",
                TokenExpiresAt = DateTime.UtcNow.AddDays(60),
            });
        }

        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private Artist NewArtist(string firstName) => new()
    {
        StudioId = _studioId,
        FirstName = firstName,
        LastName = "Test",
        Email = $"{firstName}@test.com",
    };

    [Fact]
    public async Task Handle_Owner_DeletesTokenAndSyncedPostsAndClearsVerification()
    {
        await new DisconnectInstagramHandler(_db, new FakeCurrentUser(Guid.NewGuid(), "owner"))
            .Handle(new DisconnectInstagramCommand(_artist.Id), default);

        _db.InstagramConnections.Any(c => c.ArtistId == _artist.Id).Should().BeFalse();
        _db.InstagramPosts.Any(p => p.ArtistId == _artist.Id).Should().BeFalse();

        SocialAccountLink link = _db.SocialAccountLinks.Single(l => l.SubjectId == _artist.Id);
        link.IsVerified.Should().BeFalse();
        link.EncryptedToken.Should().BeNull();
        link.TokenExpiresAt.Should().BeNull();
        link.Handle.Should().Be("ana");
    }

    [Fact]
    public async Task Handle_LeavesOtherArtistsInstagramDataUntouched()
    {
        await new DisconnectInstagramHandler(_db, new FakeCurrentUser(Guid.NewGuid(), "owner"))
            .Handle(new DisconnectInstagramCommand(_artist.Id), default);

        _db.InstagramConnections.Count(c => c.ArtistId == _otherArtist.Id).Should().Be(1);
        _db.InstagramPosts.Count(p => p.ArtistId == _otherArtist.Id).Should().Be(1);
        _db.SocialAccountLinks.Single(l => l.SubjectId == _otherArtist.Id).IsVerified.Should().BeTrue();
    }
}
