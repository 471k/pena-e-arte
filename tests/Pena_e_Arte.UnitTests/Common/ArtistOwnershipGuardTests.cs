using FluentAssertions;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Common;

public class ArtistOwnershipGuardTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private Artist AddArtist(Guid? userId)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Rui",
            LastName = "Tavares",
            Email = $"{Guid.NewGuid():N}@studio.test",
        };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        return artist;
    }

    [Fact]
    public async Task EnsureCanActAsync_ArtistOnOwnProfile_Passes()
    {
        Guid userId = Guid.NewGuid();
        Artist own = AddArtist(userId);

        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActAsync(_db, new FakeCurrentUser(userId, "artist"), own.Id, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureCanActAsync_ArtistOnAColleaguesProfileInTheSameStudio_ThrowsForbidden()
    {
        Artist colleague = AddArtist(Guid.NewGuid());
        AddArtist(Guid.NewGuid()); // the caller's own row, unrelated to `colleague`
        FakeCurrentUser caller = new(Guid.NewGuid(), "artist");

        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActAsync(_db, caller, colleague.Id, default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureCanActAsync_ArtistWithNoLinkedUserOnTheProfile_ThrowsForbidden()
    {
        // An invited-but-never-accepted artist has UserId == null; it must never match a caller.
        Artist unclaimed = AddArtist(userId: null);

        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActAsync(
            _db, new FakeCurrentUser(Guid.Empty, "artist"), unclaimed.Id, default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    public async Task EnsureCanActAsync_OwnerAndAdmin_PassOnAnyArtist(string role)
    {
        Artist someoneElse = AddArtist(Guid.NewGuid());

        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActAsync(
            _db, new FakeCurrentUser(Guid.NewGuid(), role), someoneElse.Id, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureCanActAsync_DualRoleOwnerWhoIsAlsoAnArtist_PassesOnAnyArtistBecauseTheClaimIsOwner()
    {
        Guid ownerUserId = Guid.NewGuid();
        AddArtist(ownerUserId);          // the owner's own artist profile
        Artist other = AddArtist(Guid.NewGuid());

        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActAsync(
            _db, new FakeCurrentUser(ownerUserId, "owner"), other.Id, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureCanActOnSocialSubjectAsync_StudioSubject_NeverChecksOwnership()
    {
        // Even an artist with no matching profile passes: a studio subject is only reachable
        // through owner-only endpoints, so the guard has nothing to pin an artist to.
        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActOnSocialSubjectAsync(
            _db, new FakeCurrentUser(Guid.NewGuid(), "artist"), SocialLinkSubjectType.Studio, _studioId, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureCanActOnSocialSubjectAsync_ArtistSubject_DelegatesToTheOwnershipCheck()
    {
        Artist colleague = AddArtist(Guid.NewGuid());

        Func<Task> act = () => ArtistOwnershipGuard.EnsureCanActOnSocialSubjectAsync(
            _db, new FakeCurrentUser(Guid.NewGuid(), "artist"), SocialLinkSubjectType.Artist, colleague.Id, default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
