using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Social.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

public class RequestSocialVerificationCodeHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISocialBioCheckerFactory _checkerFactory = Substitute.For<ISocialBioCheckerFactory>();
    private readonly ISocialBioChecker _checker = Substitute.For<ISocialBioChecker>();
    private readonly Guid _studioId = Guid.NewGuid();

    public RequestSocialVerificationCodeHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _checker.IsSupported.Returns(true);
        _checkerFactory.GetChecker(Arg.Any<SocialPlatform>()).Returns(_checker);
    }

    private RequestSocialVerificationCodeHandler CreateSut() => new(_db, _tenant, _checkerFactory);

    [Fact]
    public async Task Handle_UnsupportedPlatform_ThrowsBusinessRuleViolationException()
    {
        _checker.IsSupported.Returns(false);
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.TikTok,
            Handle = "studio",
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.TikTok), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoHandleSetYet_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.X), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesCodeWithExpectedPrefixAndExpiry()
    {
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.X,
            Handle = "studio",
        });
        await _db.SaveChangesAsync();

        var result = await CreateSut().Handle(
            new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.X), default);

        result.Code.Should().StartWith("PENA-");
        result.Code.Length.Should().Be("PENA-".Length + 6);
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(48), TimeSpan.FromMinutes(1));

        SocialAccountLink link = _db.SocialAccountLinks.Single();
        link.PendingVerificationCode.Should().Be(result.Code);
        link.PendingCodeExpiresAt.Should().Be(result.ExpiresAt);
    }

    [Fact]
    public async Task Handle_MismatchedStudioId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new RequestSocialVerificationCodeCommand(SocialLinkSubjectType.Studio, Guid.NewGuid(), SocialPlatform.X), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
