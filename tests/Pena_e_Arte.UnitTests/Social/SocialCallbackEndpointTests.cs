using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Social;

public class SocialCallbackEndpointTests
{
    private const string BaseUrl = "https://app.example.test";

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ISocialOAuthStateSigner _signer = Substitute.For<ISocialOAuthStateSigner>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    public SocialCallbackEndpointTests()
    {
        _appSettings.BaseUrl.Returns(BaseUrl);
    }

    private Task<IResult> Call(string platform, string? code, string? state, string? error) =>
        SocialEndpoints.HandleCallback(platform, code, state, error, _mediator, _signer, _appSettings, default);

    private void SignerDecodes(string state, SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform)
    {
        _signer.TryValidate(
                state,
                out Arg.Any<SocialLinkSubjectType>(),
                out Arg.Any<Guid>(),
                out Arg.Any<SocialPlatform>())
            .Returns(call =>
            {
                call[1] = subjectType;
                call[2] = subjectId;
                call[3] = platform;
                return true;
            });
    }

    [Fact]
    public async Task HandleCallback_DeniedForAnArtist_RedirectsToTheArtistPage()
    {
        Guid artistId = Guid.NewGuid();
        SignerDecodes("signed", SocialLinkSubjectType.Artist, artistId, SocialPlatform.TikTok);

        IResult result = await Call("tiktok", code: null, state: "signed", error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/artists/{artistId}?social=denied&platform=TikTok");
    }

    [Fact]
    public async Task HandleCallback_DeniedForAStudio_RedirectsToStudioSettingsNotTheArtistsList()
    {
        SignerDecodes("signed", SocialLinkSubjectType.Studio, Guid.NewGuid(), SocialPlatform.Facebook);

        IResult result = await Call("facebook", code: null, state: "signed", error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/studios/me?social=denied&platform=Facebook");
    }

    [Fact]
    public async Task HandleCallback_DeniedWithUndecodableState_FallsBackToTheArtistsList()
    {
        _signer.TryValidate(
                "garbage",
                out Arg.Any<SocialLinkSubjectType>(),
                out Arg.Any<Guid>(),
                out Arg.Any<SocialPlatform>())
            .Returns(false);

        IResult result = await Call("tiktok", code: null, state: "garbage", error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/artists?social=denied&platform=tiktok");
    }

    [Fact]
    public async Task HandleCallback_DeniedWithNoState_FallsBackToTheArtistsList()
    {
        IResult result = await Call("x", code: null, state: null, error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/artists?social=denied&platform=x");
    }
}
