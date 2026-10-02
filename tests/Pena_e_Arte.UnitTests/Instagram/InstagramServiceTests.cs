using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Instagram;

public class InstagramServiceTests
{
    private const string RedirectUri = "https://app.tattooos.co/api/v1/instagram/callback";

    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            (HttpStatusCode status, string json) = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static InstagramService CreateSut(ScriptedHandler? handler = null)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Instagram").Returns(_ => new HttpClient(handler ?? new ScriptedHandler((HttpStatusCode.OK, "{}"))));
        IOptions<InstagramOptions> options = Options.Create(new InstagramOptions
        {
            AppId = "app-123",
            AppSecret = "secret-xyz",
            RedirectUri = RedirectUri,
            TokenEncryptionKey = "unused",
        });
        return new InstagramService(factory, options, NullLogger<InstagramService>.Instance);
    }

    private const string LongTokenJson = """{"access_token":"LONG","token_type":"bearer","expires_in":5183944}""";

    // -- Authorization URL ------------------------------------------------------------------

    [Fact]
    public void BuildAuthorizationUrl_UsesTheCurrentInstagramLoginEndpointAndScope()
    {
        string url = CreateSut().BuildAuthorizationUrl("signed-state");

        url.Should().StartWith("https://www.instagram.com/oauth/authorize?");
        url.Should().Contain("scope=instagram_business_basic");
        url.Should().Contain("response_type=code");
        url.Should().Contain("client_id=app-123");
        url.Should().Contain("redirect_uri=" + Uri.EscapeDataString(RedirectUri));
        url.Should().Contain("state=signed-state");
    }

    [Fact]
    public void BuildAuthorizationUrl_NeverUsesTheRetiredBasicDisplayEndpointOrScopes()
    {
        // Meta shut the Basic Display API down on 2024-12-04; these values make Connect fail outright.
        string url = CreateSut().BuildAuthorizationUrl("s");

        url.Should().NotContain("api.instagram.com");
        url.Should().NotContain("instagram_basic");
        url.Should().NotContain("user_media");
    }

    // -- Code exchange --------------------------------------------------------------------------

    [Fact]
    public async Task ExchangeCodeAsync_ReadsTheWrappedDataArrayAndStringUserId()
    {
        ScriptedHandler handler = new(
            (HttpStatusCode.OK, """{"data":[{"access_token":"SHORT","user_id":"17841400000000000","permissions":"instagram_business_basic"}]}"""),
            (HttpStatusCode.OK, LongTokenJson));

        InstagramTokenResponse result = await CreateSut(handler).ExchangeCodeAsync("the-code", default);

        result.AccessToken.Should().Be("LONG");
        result.ExpiresIn.Should().Be(5183944);
        result.UserId.Should().Be("17841400000000000");
    }

    [Fact]
    public async Task ExchangeCodeAsync_SwapsTheShortTokenForALongLivedOne()
    {
        ScriptedHandler handler = new(
            (HttpStatusCode.OK, """{"data":[{"access_token":"SHORT","user_id":"1"}]}"""),
            (HttpStatusCode.OK, LongTokenJson));

        await CreateSut(handler).ExchangeCodeAsync("the-code", default);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Url.Should().Be("https://api.instagram.com/oauth/access_token");
        handler.Requests[0].Body.Should().Contain("grant_type=authorization_code").And.Contain("code=the-code");
        handler.Requests[1].Url.Should().StartWith("https://graph.instagram.com/access_token?grant_type=ig_exchange_token");
        handler.Requests[1].Url.Should().Contain("access_token=SHORT");
    }

    [Fact]
    public async Task ExchangeCodeAsync_StillAcceptsTheOldFlatShapeWithANumericUserId()
    {
        ScriptedHandler handler = new(
            (HttpStatusCode.OK, """{"access_token":"SHORT","user_id":17841400000000000}"""),
            (HttpStatusCode.OK, LongTokenJson));

        InstagramTokenResponse result = await CreateSut(handler).ExchangeCodeAsync("c", default);

        result.UserId.Should().Be("17841400000000000");
    }

    [Fact]
    public async Task ExchangeCodeAsync_StripsTheHashUnderscoreSuffixInstagramAppendsToTheCode()
    {
        ScriptedHandler handler = new(
            (HttpStatusCode.OK, """{"data":[{"access_token":"SHORT","user_id":"1"}]}"""),
            (HttpStatusCode.OK, LongTokenJson));

        await CreateSut(handler).ExchangeCodeAsync("AQabc123#_", default);

        handler.Requests[0].Body.Should().Contain("code=AQabc123").And.NotContain("%23_");
    }

    [Theory]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"user_id":"1"}]}""")]
    [InlineData("""{"user_id":"1"}""")]
    public async Task ExchangeCodeAsync_ThrowsWhenTheResponseHasNoAccessToken(string body)
    {
        ScriptedHandler handler = new((HttpStatusCode.OK, body));

        Func<Task> act = () => CreateSut(handler).ExchangeCodeAsync("c", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExchangeCodeAsync_PropagatesAnHttpFailureFromInstagram()
    {
        ScriptedHandler handler = new((HttpStatusCode.BadRequest, """{"error_message":"bad code"}"""));

        Func<Task> act = () => CreateSut(handler).ExchangeCodeAsync("c", default);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
