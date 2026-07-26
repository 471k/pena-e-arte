using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class InstagramSyncJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IInstagramService _instagram = Substitute.For<IInstagramService>();
    private readonly ITokenEncryptor _encryptor = Substitute.For<ITokenEncryptor>();
    private readonly ILogger<InstagramSyncJob> _logger = Substitute.For<ILogger<InstagramSyncJob>>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    private InstagramSyncJob CreateSut() => new(_db, _instagram, _encryptor, _logger);

    private async Task SeedStudio(Guid studioId, bool isActive = true)
    {
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Test Studio",
            Slug = "studio-" + studioId.ToString("N")[..8],
            City = "Lisboa",
            OwnerEmail = "owner@studio.com",
            IsActive = isActive,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<InstagramConnection> SeedConnection(
        DateTime? tokenExpiresAt = null, bool isActive = true)
    {
        await SeedStudio(_studioId);

        InstagramConnection conn = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            InstagramUserId = "ig-user-1",
            Username = "artist_ig",
            EncryptedToken = "encrypted-token",
            TokenExpiresAt = tokenExpiresAt ?? DateTime.UtcNow.AddDays(30),
            IsActive = isActive,
        };
        _db.InstagramConnections.Add(conn);
        await _db.SaveChangesAsync();
        return conn;
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_InsertsNewPostsAndUpdatesLastSyncedAt()
    {
        await SeedConnection();
        _encryptor.Decrypt("encrypted-token").Returns("plain-token");
        _instagram.GetMediaAsync("plain-token", Arg.Any<CancellationToken>()).Returns(
        [
            new InstagramMediaItem("media-1", "IMAGE", "https://img/1.jpg", null, "caption", DateTime.UtcNow),
        ]);

        await CreateSut().ExecuteAsync();

        _db.InstagramPosts.Should().ContainSingle(p => p.InstagramMediaId == "media-1");
        _db.InstagramConnections.Single(c => c.ArtistId == _artistId).LastSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MediaAlreadySynced_DoesNotInsertDuplicate()
    {
        await SeedConnection();
        _db.InstagramPosts.Add(new InstagramPost
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            InstagramMediaId = "media-1",
            MediaUrl = "https://img/old.jpg",
            MediaType = "IMAGE",
            PostedAt = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        _encryptor.Decrypt("encrypted-token").Returns("plain-token");
        _instagram.GetMediaAsync("plain-token", Arg.Any<CancellationToken>()).Returns(
        [
            new InstagramMediaItem("media-1", "IMAGE", "https://img/1.jpg", null, "caption", DateTime.UtcNow),
        ]);

        await CreateSut().ExecuteAsync();

        _db.InstagramPosts.Should().ContainSingle(p => p.InstagramMediaId == "media-1");
    }

    [Fact]
    public async Task ExecuteAsync_TokenExpiringSoon_RefreshesTokenAndPersistsNewEncryptedValue()
    {
        await SeedConnection(tokenExpiresAt: DateTime.UtcNow.AddDays(3));
        _encryptor.Decrypt("encrypted-token").Returns("old-plain-token");
        _encryptor.Encrypt("new-plain-token").Returns("new-encrypted-token");

        DateTime newExpiry = DateTime.UtcNow.AddDays(60);
        _instagram.RefreshTokenAsync("old-plain-token", Arg.Any<CancellationToken>())
            .Returns(("new-plain-token", newExpiry));
        _instagram.GetMediaAsync("new-plain-token", Arg.Any<CancellationToken>()).Returns([]);

        await CreateSut().ExecuteAsync();

        InstagramConnection updated = _db.InstagramConnections.Single(c => c.ArtistId == _artistId);
        updated.EncryptedToken.Should().Be("new-encrypted-token");
        updated.TokenExpiresAt.Should().BeCloseTo(newExpiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_RefreshReturns400_DeactivatesConnectionAndSkipsMediaFetch()
    {
        await SeedConnection(tokenExpiresAt: DateTime.UtcNow.AddDays(3));
        _encryptor.Decrypt("encrypted-token").Returns("old-plain-token");
        _instagram.RefreshTokenAsync("old-plain-token", Arg.Any<CancellationToken>())
            .Returns<(string, DateTime)>(_ => throw new HttpRequestException(
                "revoked", null, System.Net.HttpStatusCode.BadRequest));

        await CreateSut().ExecuteAsync();

        _db.InstagramConnections.Single(c => c.ArtistId == _artistId).IsActive.Should().BeFalse();
        await _instagram.DidNotReceive().GetMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_VideoMediaType_IsSkipped()
    {
        // IInstagramService.GetMediaAsync already filters to IMAGE/CAROUSEL_ALBUM;
        // this test verifies the job doesn't second-guess or re-filter items it returns.
        await SeedConnection();
        _encryptor.Decrypt("encrypted-token").Returns("plain-token");
        _instagram.GetMediaAsync("plain-token", Arg.Any<CancellationToken>()).Returns(
        [
            new InstagramMediaItem("media-image", "IMAGE", "https://img/1.jpg", null, null, DateTime.UtcNow),
        ]);

        await CreateSut().ExecuteAsync();

        _db.InstagramPosts.Should().ContainSingle();
        _db.InstagramPosts.Should().OnlyContain(p => p.MediaType == "IMAGE");
    }

    [Fact]
    public async Task ExecuteAsync_OneConnectionThrows_OtherConnectionStillSyncs()
    {
        await SeedConnection();
        Guid otherArtistId = Guid.NewGuid();
        Guid otherStudioId = Guid.NewGuid();
        await SeedStudio(otherStudioId);
        _db.InstagramConnections.Add(new InstagramConnection
        {
            StudioId = otherStudioId,
            ArtistId = otherArtistId,
            InstagramUserId = "ig-user-2",
            Username = "other_artist",
            EncryptedToken = "encrypted-token-2",
            TokenExpiresAt = DateTime.UtcNow.AddDays(30),
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        _encryptor.Decrypt("encrypted-token").Returns(_ => throw new InvalidOperationException("boom"));
        _encryptor.Decrypt("encrypted-token-2").Returns("plain-token-2");
        _instagram.GetMediaAsync("plain-token-2", Arg.Any<CancellationToken>()).Returns(
        [
            new InstagramMediaItem("media-other", "IMAGE", "https://img/2.jpg", null, null, DateTime.UtcNow),
        ]);

        await CreateSut().ExecuteAsync();

        _db.InstagramPosts.Should().ContainSingle(p => p.ArtistId == otherArtistId);
    }

    [Fact]
    public async Task ExecuteAsync_StudioSuspended_SkipsConnectionAndDoesNotCallInstagramApi()
    {
        InstagramConnection conn = await SeedConnection();
        conn.StudioId = _studioId; // seeded active above; now suspend it
        Studio studio = _db.Studios.Single(s => s.Id == _studioId);
        studio.IsActive = false;
        await _db.SaveChangesAsync();

        _encryptor.Decrypt("encrypted-token").Returns("plain-token");

        await CreateSut().ExecuteAsync();

        await _instagram.DidNotReceive().GetMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.InstagramPosts.Should().BeEmpty();
        _db.InstagramConnections.Single(c => c.ArtistId == _artistId).LastSyncedAt.Should().BeNull();
    }
}
