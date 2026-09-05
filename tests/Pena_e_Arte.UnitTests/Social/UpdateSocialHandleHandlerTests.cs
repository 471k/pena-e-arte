using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

public class UpdateSocialHandleHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public UpdateSocialHandleHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private UpdateSocialHandleHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NoExistingLink_CreatesUnverifiedLink()
    {
        await CreateSut().Handle(
            new UpdateSocialHandleCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.X, "@myhandle"), default);

        SocialAccountLink link = _db.SocialAccountLinks.Single();
        link.Handle.Should().Be("myhandle"); // leading '@' stripped
        link.IsVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ChangingHandleOnVerifiedLink_UnverifiesIt()
    {
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.X,
            Handle = "oldhandle",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow,
            VerificationMethod = SocialVerificationMethod.ManualBioCode,
        });
        await _db.SaveChangesAsync();

        await CreateSut().Handle(
            new UpdateSocialHandleCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.X, "newhandle"), default);

        SocialAccountLink link = _db.SocialAccountLinks.Single();
        link.Handle.Should().Be("newhandle");
        link.IsVerified.Should().BeFalse();
        link.VerifiedAt.Should().BeNull();
        link.VerificationMethod.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SettingSameHandleAgain_LeavesVerificationUntouched()
    {
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.X,
            Handle = "samehandle",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow,
            VerificationMethod = SocialVerificationMethod.ManualBioCode,
        });
        await _db.SaveChangesAsync();

        await CreateSut().Handle(
            new UpdateSocialHandleCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.X, "samehandle"), default);

        _db.SocialAccountLinks.Single().IsVerified.Should().BeTrue();
    }
}
