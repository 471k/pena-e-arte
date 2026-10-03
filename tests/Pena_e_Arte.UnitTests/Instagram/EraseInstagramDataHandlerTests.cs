using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Instagram;

public class EraseInstagramDataHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _otherArtistId = Guid.NewGuid();

    public EraseInstagramDataHandlerTests()
    {
        Seed(_artistId, "ig-ana");
        Seed(_otherArtistId, "ig-rui");
        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private void Seed(Guid artistId, string instagramUserId)
    {
        _db.InstagramConnections.Add(new InstagramConnection
        {
            StudioId = _studioId,
            ArtistId = artistId,
            InstagramUserId = instagramUserId,
            InstagramAccountId = "acct-" + instagramUserId,
            Username = instagramUserId,
            EncryptedToken = "encrypted-token",
            TokenExpiresAt = DateTime.UtcNow.AddDays(60),
        });
        _db.InstagramPosts.Add(new InstagramPost
        {
            StudioId = _studioId,
            ArtistId = artistId,
            InstagramMediaId = "media-" + instagramUserId,
            MediaUrl = "https://cdn.test/a.jpg",
            PostedAt = DateTime.UtcNow,
            IsVisible = true,
        });
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Artist,
            SubjectId = artistId,
            Platform = SocialPlatform.Instagram,
            Handle = instagramUserId,
            IsVerified = true,
            ExternalUserId = instagramUserId,
            AlternateExternalUserId = "acct-" + instagramUserId,
            EncryptedToken = "encrypted-token",
            TokenExpiresAt = DateTime.UtcNow.AddDays(60),
        });
    }

    private EraseInstagramDataHandler CreateSut() =>
        new(_db, NullLogger<EraseInstagramDataHandler>.Instance);

    [Fact]
    public async Task Handle_KnownInstagramUser_DeletesTokenAndPostsAndClearsVerification()
    {
        int erased = await CreateSut().Handle(new EraseInstagramDataCommand("ig-ana"), default);

        erased.Should().Be(1);
        _db.InstagramConnections.Any(c => c.ArtistId == _artistId).Should().BeFalse();
        _db.InstagramPosts.Any(p => p.ArtistId == _artistId).Should().BeFalse();

        SocialAccountLink link = _db.SocialAccountLinks.Single(l => l.SubjectId == _artistId);
        link.IsVerified.Should().BeFalse();
        link.ExternalUserId.Should().BeNull();
        link.EncryptedToken.Should().BeNull();
        link.TokenExpiresAt.Should().BeNull();
        link.Handle.Should().Be("ig-ana");
    }

    // Production regression: Meta's callback can carry the professional account id instead of the
    // app-scoped one the token exchange stored, which used to match nothing.
    [Fact]
    public async Task Handle_ProfessionalAccountId_ErasesTheSameDataAsTheAppScopedId()
    {
        int erased = await CreateSut().Handle(new EraseInstagramDataCommand("acct-ig-ana"), default);

        erased.Should().Be(1);
        _db.InstagramConnections.Any(c => c.ArtistId == _artistId).Should().BeFalse();
        _db.InstagramPosts.Any(p => p.ArtistId == _artistId).Should().BeFalse();

        SocialAccountLink link = _db.SocialAccountLinks.Single(l => l.SubjectId == _artistId);
        link.IsVerified.Should().BeFalse();
        link.ExternalUserId.Should().BeNull();
        link.AlternateExternalUserId.Should().BeNull();
        link.EncryptedToken.Should().BeNull();

        _db.InstagramConnections.Count(c => c.ArtistId == _otherArtistId).Should().Be(1);
    }

    [Fact]
    public async Task Handle_RowConnectedBeforeTheProfessionalIdExisted_StillMatchesTheAppScopedId()
    {
        Guid legacyArtistId = Guid.NewGuid();
        _db.InstagramConnections.Add(new InstagramConnection
        {
            StudioId = _studioId,
            ArtistId = legacyArtistId,
            InstagramUserId = "ig-legacy",
            Username = "legacy",
            EncryptedToken = "encrypted-token",
            TokenExpiresAt = DateTime.UtcNow.AddDays(60),
        });
        await _db.SaveChangesAsync();

        int erased = await CreateSut().Handle(new EraseInstagramDataCommand("ig-legacy"), default);

        erased.Should().Be(1);
        _db.InstagramConnections.Any(c => c.ArtistId == legacyArtistId).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LeavesEveryOtherInstagramUsersDataUntouched()
    {
        await CreateSut().Handle(new EraseInstagramDataCommand("ig-ana"), default);

        _db.InstagramConnections.Count(c => c.ArtistId == _otherArtistId).Should().Be(1);
        _db.InstagramPosts.Count(p => p.ArtistId == _otherArtistId).Should().Be(1);
        _db.SocialAccountLinks.Single(l => l.SubjectId == _otherArtistId).IsVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownInstagramUser_ErasesNothingAndDoesNotThrow()
    {
        int erased = await CreateSut().Handle(new EraseInstagramDataCommand("not-connected"), default);

        erased.Should().Be(0);
        _db.InstagramConnections.Count().Should().Be(2);
        _db.InstagramPosts.Count().Should().Be(2);
    }

    [Fact]
    public async Task Handle_WritesAnAuditEntryWithoutTheInstagramUserIdOrUsername()
    {
        await CreateSut().Handle(new EraseInstagramDataCommand("ig-ana"), default);

        AuditLogEntry entry = _db.AuditLogEntries.Single();
        entry.Action.Should().Be(AuditActions.SocialDisconnected);
        entry.TargetId.Should().Be(_artistId);
        entry.StudioId.Should().Be(_studioId);
        entry.ActorRole.Should().Be("meta-callback");
        entry.Metadata.Should().NotContain("ig-ana");
    }

    [Fact]
    public async Task Handle_ConnectionWithoutALink_StillErasedAndAudited()
    {
        Guid orphanArtistId = Guid.NewGuid();
        _db.InstagramConnections.Add(new InstagramConnection
        {
            StudioId = _studioId,
            ArtistId = orphanArtistId,
            InstagramUserId = "ig-orphan",
            Username = "orphan",
            EncryptedToken = "encrypted-token",
            TokenExpiresAt = DateTime.UtcNow.AddDays(60),
        });
        await _db.SaveChangesAsync();

        int erased = await CreateSut().Handle(new EraseInstagramDataCommand("ig-orphan"), default);

        erased.Should().Be(1);
        _db.InstagramConnections.Any(c => c.ArtistId == orphanArtistId).Should().BeFalse();
        _db.AuditLogEntries.Single(a => a.TargetId == orphanArtistId).Action.Should().Be(AuditActions.SocialDisconnected);
    }
}
