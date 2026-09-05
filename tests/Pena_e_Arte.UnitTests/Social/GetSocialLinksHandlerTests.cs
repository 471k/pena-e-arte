using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Social.Queries;
using Pena_e_Arte.Contracts.Responses.Social;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

public class GetSocialLinksHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISocialOAuthProviderFactory _providerFactory = Substitute.For<ISocialOAuthProviderFactory>();
    private readonly ISocialBioCheckerFactory _checkerFactory = Substitute.For<ISocialBioCheckerFactory>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetSocialLinksHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);

        ISocialOAuthProvider provider = Substitute.For<ISocialOAuthProvider>();
        provider.IsConfigured.Returns(true);
        _providerFactory.GetProvider(Arg.Any<SocialPlatform>()).Returns(provider);

        ISocialBioChecker checker = Substitute.For<ISocialBioChecker>();
        checker.IsSupported.Returns(true);
        _checkerFactory.GetChecker(Arg.Any<SocialPlatform>()).Returns(checker);
    }

    private GetSocialLinksHandler CreateSut() => new(_db, _tenant, _providerFactory, _checkerFactory);

    [Fact]
    public async Task Handle_ReturnsOneRowPerPlatform_EvenWithNoLinksYet()
    {
        List<SocialLinkStatusResponse> result = await CreateSut().Handle(
            new GetSocialLinksQuery(SocialLinkSubjectType.Studio, _studioId), default);

        result.Should().HaveCount(Enum.GetValues<SocialPlatform>().Length);
        result.Should().OnlyContain(r => r.IsVerified == false && r.Handle == null);
    }

    [Fact]
    public async Task Handle_ExistingVerifiedLink_ReportedInResult()
    {
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = _studioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = _studioId,
            Platform = SocialPlatform.Instagram,
            Handle = "studiohandle",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow,
            VerificationMethod = SocialVerificationMethod.OAuthConnect,
        });
        await _db.SaveChangesAsync();

        List<SocialLinkStatusResponse> result = await CreateSut().Handle(
            new GetSocialLinksQuery(SocialLinkSubjectType.Studio, _studioId), default);

        SocialLinkStatusResponse igRow = result.Single(r => r.Platform == "Instagram");
        igRow.Handle.Should().Be("studiohandle");
        igRow.IsVerified.Should().BeTrue();
        igRow.VerificationMethod.Should().Be("OAuthConnect");
    }

    [Fact]
    public async Task Handle_AnotherStudiosLinks_NotVisible()
    {
        Guid otherStudioId = Guid.NewGuid();
        _db.SocialAccountLinks.Add(new SocialAccountLink
        {
            StudioId = otherStudioId,
            SubjectType = SocialLinkSubjectType.Studio,
            SubjectId = otherStudioId,
            Platform = SocialPlatform.Instagram,
            Handle = "someone-elses-studio",
            IsVerified = true,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new GetSocialLinksQuery(SocialLinkSubjectType.Studio, otherStudioId), default);

        // The caller's tenant is _studioId, not otherStudioId — SocialSubjectResolver must
        // reject this exactly like UpdateStudioBrandingCommand rejects a mismatched StudioId.
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
