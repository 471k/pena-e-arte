using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Instagram;

public class InstagramCallbackEndpointTests
{
    private const string BaseUrl = "https://app.example.test";

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IInstagramStateSigner _signer = Substitute.For<IInstagramStateSigner>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    public InstagramCallbackEndpointTests()
    {
        _appSettings.BaseUrl.Returns(BaseUrl);
    }

    private Task<IResult> Call(string? code, string? state, string? error) =>
        InstagramEndpoints.HandleCallback(code, state, error, _mediator, _signer, _appSettings, default);

    [Fact]
    public async Task HandleCallback_DeniedWithDecodableState_RedirectsToTheArtistPage()
    {
        Guid artistId = Guid.NewGuid();
        _signer.TryValidate("signed-state", out Arg.Any<Guid>())
            .Returns(call => { call[1] = artistId; return true; });

        IResult result = await Call(code: null, state: "signed-state", error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/artists/{artistId}?instagram=denied");
    }

    [Fact]
    public async Task HandleCallback_DeniedWithUndecodableState_FallsBackToTheArtistsList()
    {
        _signer.TryValidate("garbage", out Arg.Any<Guid>()).Returns(false);

        IResult result = await Call(code: null, state: "garbage", error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/artists?instagram=denied");
    }

    [Fact]
    public async Task HandleCallback_DeniedWithNoState_FallsBackToTheArtistsList()
    {
        IResult result = await Call(code: null, state: null, error: "access_denied");

        result.Should().BeOfType<RedirectHttpResult>()
            .Which.Url.Should().Be($"{BaseUrl}/artists?instagram=denied");
    }

    [Fact]
    public async Task HandleCallback_CodeButUndecodableState_ReturnsBadRequest()
    {
        _signer.TryValidate("garbage", out Arg.Any<Guid>()).Returns(false);

        IResult result = await Call(code: "abc", state: "garbage", error: null);

        result.Should().BeOfType<BadRequest<string>>();
    }
}
