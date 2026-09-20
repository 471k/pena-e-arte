using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Application.Instagram.Queries;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Application.Social.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

/// <summary>
/// The artist-self-service authorization matrix for the seven artist-scoped connect/verify/disconnect
/// operations: an artist may act on their OWN profile only, owner and admin on any artist in the
/// tenant, and an id that doesn't resolve is a NotFound before it can ever be a Forbidden. (The
/// tenant query filter itself is exercised against a real database in the integration tests —
/// the in-memory context here has no HasQueryFilter to prove it with.)
/// </summary>
public class ArtistSocialSelfServiceHandlerTests
{
    public const string InstagramConnectUrl = "instagram-connect-url";
    public const string InstagramDisconnect = "instagram-disconnect";
    public const string SocialConnectUrl = "social-connect-url";
    public const string SocialHandle = "social-handle";
    public const string SocialRequestCode = "social-request-code";
    public const string SocialVerifyCode = "social-verify-code";
    public const string SocialDisconnect = "social-disconnect";

    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IInstagramService _instagram = Substitute.For<IInstagramService>();
    private readonly IInstagramStateSigner _instagramSigner = Substitute.For<IInstagramStateSigner>();
    private readonly ISocialOAuthProviderFactory _providerFactory = Substitute.For<ISocialOAuthProviderFactory>();
    private readonly ISocialOAuthProvider _provider = Substitute.For<ISocialOAuthProvider>();
    private readonly ISocialOAuthStateSigner _socialSigner = Substitute.For<ISocialOAuthStateSigner>();
    private readonly ISocialBioCheckerFactory _checkerFactory = Substitute.For<ISocialBioCheckerFactory>();
    private readonly ISocialBioChecker _checker = Substitute.For<ISocialBioChecker>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ArtistSocialSelfServiceHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _provider.IsConfigured.Returns(true);
        _providerFactory.GetProvider(Arg.Any<SocialPlatform>()).Returns(_provider);
        _checker.IsSupported.Returns(true);
        _checker.BioContainsCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _checkerFactory.GetChecker(Arg.Any<SocialPlatform>()).Returns(_checker);
    }

    public static IEnumerable<object[]> Operations() =>
    [
        [InstagramConnectUrl], [InstagramDisconnect], [SocialConnectUrl],
        [SocialHandle], [SocialRequestCode], [SocialVerifyCode], [SocialDisconnect],
    ];

    /// <summary>An artist with a TikTok link that already has a handle and a live pending code, so every operation can succeed.</summary>
    private async Task<Artist> SeedArtist(Guid? userId)
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
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Artist,
            SubjectId = artist.Id,
            Platform = SocialPlatform.TikTok,
            Handle = "original",
            PendingVerificationCode = "PENA-ABC123",
            PendingCodeExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        await _db.SaveChangesAsync();
        return artist;
    }

    private Task Run(string operation, FakeCurrentUser user, Guid artistId) => operation switch
    {
        InstagramConnectUrl => new GetInstagramConnectUrlHandler(_db, _instagram, _instagramSigner, user)
            .Handle(new GetInstagramConnectUrlQuery(artistId), default),
        InstagramDisconnect => new DisconnectInstagramHandler(_db, user)
            .Handle(new DisconnectInstagramCommand(artistId), default),
        SocialConnectUrl => new GetSocialConnectUrlHandler(_db, _tenant, _providerFactory, _socialSigner, user)
            .Handle(new GetSocialConnectUrlQuery(SocialLinkSubjectType.Artist, artistId, SocialPlatform.TikTok), default),
        SocialHandle => new UpdateSocialHandleHandler(_db, _tenant, user)
            .Handle(new UpdateSocialHandleCommand(SocialLinkSubjectType.Artist, artistId, SocialPlatform.TikTok, "changed"), default),
        SocialRequestCode => new RequestSocialVerificationCodeHandler(_db, _tenant, _checkerFactory, user)
            .Handle(new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Artist, artistId, SocialPlatform.TikTok), default),
        SocialVerifyCode => new VerifySocialBioCodeHandler(_db, _tenant, _checkerFactory, user, NullLogger<VerifySocialBioCodeHandler>.Instance)
            .Handle(new VerifySocialBioCodeCommand(SocialLinkSubjectType.Artist, artistId, SocialPlatform.TikTok), default),
        SocialDisconnect => new DisconnectSocialAccountHandler(_db, _tenant, user)
            .Handle(new DisconnectSocialAccountCommand(SocialLinkSubjectType.Artist, artistId, SocialPlatform.TikTok), default),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ArtistActingOnTheirOwnProfile_Succeeds(string operation)
    {
        Guid userId = Guid.NewGuid();
        Artist own = await SeedArtist(userId);

        Func<Task> act = () => Run(operation, new FakeCurrentUser(userId, "artist"), own.Id);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ArtistActingOnAColleagueInTheSameStudio_IsForbidden(string operation)
    {
        Artist colleague = await SeedArtist(Guid.NewGuid());
        FakeCurrentUser caller = new(Guid.NewGuid(), "artist");

        Func<Task> act = () => Run(operation, caller, colleague.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task OwnerAndAdmin_CanActOnAnyArtistInTheTenant(string operation)
    {
        // Separate artists: verify-code consumes the pending code, so a second run on the same
        // artist would fail for a reason unrelated to authorization.
        Artist forOwner = await SeedArtist(Guid.NewGuid());
        Artist forAdmin = await SeedArtist(Guid.NewGuid());

        Func<Task> asOwner = () => Run(operation, FakeCurrentUser.Owner(), forOwner.Id);
        Func<Task> asAdmin = () => Run(operation, FakeCurrentUser.Admin(), forAdmin.Id);

        await asOwner.Should().NotThrowAsync();
        await asAdmin.Should().NotThrowAsync();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task DualRoleOwnerWhoIsAlsoAnArtist_IsNotHeldToTheOwnProfileRule(string operation)
    {
        Guid ownerUserId = Guid.NewGuid();
        await SeedArtist(ownerUserId);            // the owner's own artist profile
        Artist other = await SeedArtist(Guid.NewGuid());

        Func<Task> act = () => Run(operation, new FakeCurrentUser(ownerUserId, "owner"), other.Id);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task AnArtistIdThatDoesNotResolve_IsNotFoundNotForbidden(string operation)
    {
        Func<Task> act = () => Run(operation, new FakeCurrentUser(Guid.NewGuid(), "artist"), Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ForbiddenHandleUpdate_LeavesTheColleaguesLinkUntouched()
    {
        Artist colleague = await SeedArtist(Guid.NewGuid());

        Func<Task> act = () => Run(SocialHandle, new FakeCurrentUser(Guid.NewGuid(), "artist"), colleague.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
        _db.SocialAccountLinks.Single(l => l.SubjectId == colleague.Id).Handle.Should().Be("original");
    }

    [Fact]
    public async Task ForbiddenDisconnect_LeavesAVerifiedLinkVerified()
    {
        Artist colleague = await SeedArtist(Guid.NewGuid());
        SocialAccountLink link = _db.SocialAccountLinks.Single(l => l.SubjectId == colleague.Id);
        link.IsVerified = true;
        await _db.SaveChangesAsync();

        Func<Task> act = () => Run(SocialDisconnect, new FakeCurrentUser(Guid.NewGuid(), "artist"), colleague.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
        _db.SocialAccountLinks.Single(l => l.SubjectId == colleague.Id).IsVerified.Should().BeTrue();
    }

    [Fact]
    public async Task ForbiddenConnectUrl_NeverSignsAStateForTheColleague()
    {
        Artist colleague = await SeedArtist(Guid.NewGuid());

        Func<Task> igAct = () => Run(InstagramConnectUrl, new FakeCurrentUser(Guid.NewGuid(), "artist"), colleague.Id);
        Func<Task> socialAct = () => Run(SocialConnectUrl, new FakeCurrentUser(Guid.NewGuid(), "artist"), colleague.Id);

        await igAct.Should().ThrowAsync<ForbiddenException>();
        await socialAct.Should().ThrowAsync<ForbiddenException>();
        _instagramSigner.DidNotReceiveWithAnyArgs().Sign(default);
        _socialSigner.DidNotReceiveWithAnyArgs().Sign(default, default, default);
    }

    [Fact]
    public async Task StudioSubject_IsNotAffectedByTheOwnershipGuard()
    {
        // An artist role can't reach the studio endpoints (OwnerOnly), but if a caller with no
        // matching artist profile ever got this far the guard must not be the thing that blocks a
        // studio subject — the handler's own studio-id check is what protects it.
        Func<Task> act = () => new UpdateSocialHandleHandler(_db, _tenant, new FakeCurrentUser(Guid.NewGuid(), "artist"))
            .Handle(new UpdateSocialHandleCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.X, "studio"), default);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(SocialHandle)]
    [InlineData(SocialRequestCode)]
    [InlineData(SocialVerifyCode)]
    public async Task ArtistInstagramThroughTheGenericPath_IsRejectedLikeConnectUrlAndDisconnectAlreadyAre(string operation)
    {
        Guid userId = Guid.NewGuid();
        Artist own = await SeedArtist(userId);
        FakeCurrentUser user = new(userId, "artist");

        Func<Task> act = () => operation switch
        {
            SocialHandle => new UpdateSocialHandleHandler(_db, _tenant, user)
                .Handle(new UpdateSocialHandleCommand(SocialLinkSubjectType.Artist, own.Id, SocialPlatform.Instagram, "x"), default),
            SocialRequestCode => new RequestSocialVerificationCodeHandler(_db, _tenant, _checkerFactory, user)
                .Handle(new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Artist, own.Id, SocialPlatform.Instagram), default),
            _ => new VerifySocialBioCodeHandler(_db, _tenant, _checkerFactory, user, NullLogger<VerifySocialBioCodeHandler>.Instance)
                .Handle(new VerifySocialBioCodeCommand(SocialLinkSubjectType.Artist, own.Id, SocialPlatform.Instagram), default),
        };

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _db.SocialAccountLinks.Any(l => l.Platform == SocialPlatform.Instagram).Should().BeFalse();
    }
}
