using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Instagram;

public class InstagramMetaCallbackEndpointTests
{
    private const string BaseUrl = "https://app.example.test";

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IMetaSignedRequestParser _parser = Substitute.For<IMetaSignedRequestParser>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    public InstagramMetaCallbackEndpointTests()
    {
        _appSettings.BaseUrl.Returns(BaseUrl);
    }

    private void ParserAccepts(string signedRequest, string instagramUserId) =>
        _parser.TryGetUserId(signedRequest, out Arg.Any<string>())
            .Returns(call => { call[1] = instagramUserId; return true; });

    [Fact]
    public async Task Deauthorize_ValidSignedRequest_ErasesThatUsersDataAndReturnsOk()
    {
        ParserAccepts("signed", "ig-1");

        IResult result = await InstagramEndpoints.HandleDeauthorize("signed", _parser, _mediator, default);

        result.Should().BeOfType<Ok>();
        await _mediator.Received(1).Send(
            Arg.Is<EraseInstagramDataCommand>(c => c.InstagramUserId == "ig-1"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("forged")]
    public async Task Deauthorize_MissingOrInvalidSignedRequest_ReturnsBadRequestAndErasesNothing(string? signedRequest)
    {
        _parser.TryGetUserId(Arg.Any<string>(), out Arg.Any<string>()).Returns(false);

        IResult result = await InstagramEndpoints.HandleDeauthorize(signedRequest, _parser, _mediator, default);

        result.Should().BeOfType<BadRequest<string>>();
        await _mediator.DidNotReceive().Send(Arg.Any<EraseInstagramDataCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataDeletion_ValidSignedRequest_ErasesAndReturnsMetasRequiredShape()
    {
        ParserAccepts("signed", "ig-1");

        IResult result = await InstagramEndpoints.HandleDataDeletion("signed", _parser, _mediator, _appSettings, default);

        InstagramDataDeletionResponse body = result.Should().BeOfType<Ok<InstagramDataDeletionResponse>>().Subject.Value!;
        body.ConfirmationCode.Should().MatchRegex("^[0-9a-f]{32}$");
        body.Url.Should().Be($"{BaseUrl}/data-deletion/instagram?code={body.ConfirmationCode}");
        body.Url.Should().NotContain("ig-1");
        await _mediator.Received(1).Send(
            Arg.Is<EraseInstagramDataCommand>(c => c.InstagramUserId == "ig-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataDeletion_UnknownInstagramUser_StillReturnsAConfirmation()
    {
        ParserAccepts("signed", "never-connected");
        _mediator.Send(Arg.Any<EraseInstagramDataCommand>(), Arg.Any<CancellationToken>()).Returns(0);

        IResult result = await InstagramEndpoints.HandleDataDeletion("signed", _parser, _mediator, _appSettings, default);

        result.Should().BeOfType<Ok<InstagramDataDeletionResponse>>();
    }

    [Fact]
    public async Task DataDeletion_InvalidSignedRequest_ReturnsBadRequestAndErasesNothing()
    {
        _parser.TryGetUserId(Arg.Any<string>(), out Arg.Any<string>()).Returns(false);

        IResult result = await InstagramEndpoints.HandleDataDeletion("forged", _parser, _mediator, _appSettings, default);

        result.Should().BeOfType<BadRequest<string>>();
        await _mediator.DidNotReceive().Send(Arg.Any<EraseInstagramDataCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DataDeletionResponse_SerializesWithMetasSnakeCaseNames()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new InstagramDataDeletionResponse("https://x/y", "abc"));

        json.Should().Be("{\"url\":\"https://x/y\",\"confirmation_code\":\"abc\"}");
    }
}
