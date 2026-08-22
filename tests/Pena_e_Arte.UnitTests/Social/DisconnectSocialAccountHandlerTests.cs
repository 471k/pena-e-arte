using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

public class DisconnectSocialAccountHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public DisconnectSocialAccountHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private DisconnectSocialAccountHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ArtistInstagram_ThrowsBusinessRuleViolationException()
    {
        // Instagram-artist disconnects must go through DisconnectInstagramCommand, which
        // also deactivates InstagramConnection — this generic path must never touch it.
        Func<Task> act = () => CreateSut().Handle(
            new DisconnectSocialAccountCommand(SocialLinkSubjectType.Artist, Guid.NewGuid(), SocialPlatform.Instagram), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoExistingLink_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(
            new DisconnectSocialAccountCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.TikTok), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_VerifiedLink_ClearsVerificationButKeepsHandle()
    {
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.TikTok,
            Handle = "studiohandle",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow,
            VerificationMethod = SocialVerificationMethod.OAuthConnect,
            ExternalUserId = "ext-1",
            EncryptedToken = "encrypted",
            TokenExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync();

        await CreateSut().Handle(
            new DisconnectSocialAccountCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.TikTok), default);

        SocialAccountLink link = _db.SocialAccountLinks.Single();
        link.Handle.Should().Be("studiohandle");
        link.IsVerified.Should().BeFalse();
        link.VerifiedAt.Should().BeNull();
        link.VerificationMethod.Should().BeNull();
        link.ExternalUserId.Should().BeNull();
        link.EncryptedToken.Should().BeNull();
        link.TokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MismatchedStudioId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new DisconnectSocialAccountCommand(SocialLinkSubjectType.Studio, Guid.NewGuid(), SocialPlatform.X), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
