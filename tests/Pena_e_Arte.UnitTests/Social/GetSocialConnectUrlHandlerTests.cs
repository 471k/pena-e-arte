using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Social.Queries;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Social;

public class GetSocialConnectUrlHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISocialOAuthProviderFactory _providerFactory = Substitute.For<ISocialOAuthProviderFactory>();
    private readonly ISocialOAuthStateSigner _stateSigner = Substitute.For<ISocialOAuthStateSigner>();
    private readonly ISocialOAuthProvider _provider = Substitute.For<ISocialOAuthProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetSocialConnectUrlHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _providerFactory.GetProvider(Arg.Any<SocialPlatform>()).Returns(_provider);
    }

    private GetSocialConnectUrlHandler CreateSut() => new(_db, _tenant, _providerFactory, _stateSigner);

    [Fact]
    public async Task Handle_ArtistInstagram_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new GetSocialConnectUrlQuery(SocialLinkSubjectType.Artist, Guid.NewGuid(), SocialPlatform.Instagram), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ProviderNotConfigured_ThrowsConflictException()
    {
        _provider.IsConfigured.Returns(false);

        Func<Task> act = () => CreateSut().Handle(
            new GetSocialConnectUrlQuery(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.Facebook), default);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_MismatchedStudioId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new GetSocialConnectUrlQuery(SocialLinkSubjectType.Studio, Guid.NewGuid(), SocialPlatform.TikTok), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_Valid_ReturnsProviderAuthorizationUrlSignedWithSubjectState()
    {
        _provider.IsConfigured.Returns(true);
        _provider.BuildAuthorizationUrl("signed-state").Returns("https://tiktok.com/authorize?state=signed-state");
        _stateSigner.Sign(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.TikTok).Returns("signed-state");

        var result = await CreateSut().Handle(
            new GetSocialConnectUrlQuery(SocialLinkSubjectType.Studio, _studioId, SocialPlatform.TikTok), default);

        result.AuthUrl.Should().Be("https://tiktok.com/authorize?state=signed-state");
    }
}
