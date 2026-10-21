using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.Pok;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// C-7: the owner's POK keys are tested with one login, and the merchant id with one read of a random order
/// under it, before anything is saved. Status codes as measured on POK staging, 2026-10-21.
/// </summary>
public class PokCredentialVerifierTests
{
    private const string LoginOk = """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}""";

    private static PokCredentialVerifier CreateSut(FakeHttpMessageHandler handler)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Pok").Returns(_ => new HttpClient(handler));
        return new PokCredentialVerifier(
            Options.Create(new PokOptions { BaseUrl = "https://api-staging.pokpay.io" }),
            factory, NullLogger<PokCredentialVerifier>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>Login answers <paramref name="login"/>; the merchant read answers <paramref name="merchantRead"/>.</summary>
    private static FakeHttpMessageHandler Pok(HttpResponseMessage login, HttpStatusCode merchantRead = HttpStatusCode.NotFound) =>
        new(req => req.RequestUri!.AbsolutePath == "/auth/sdk/login"
            ? login
            : Json(merchantRead, """{"message":"x"}"""));

    [Fact]
    public async Task ValidKeysAndOwnMerchant_IsValid_LoginThenOneReadOnlyOrderLookupUnderThatMerchant()
    {
        FakeHttpMessageHandler handler = Pok(Json(HttpStatusCode.OK, LoginOk), HttpStatusCode.NotFound);

        PokCredentialCheckResult result = await CreateSut(handler).CheckAsync("key-1", "secret-1", "merchant-1", default);

        result.Should().Be(PokCredentialCheckResult.Valid);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Path.Should().Be("/auth/sdk/login");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Body.Should().Contain("\"keyId\":\"key-1\"").And.Contain("\"keySecret\":\"secret-1\"");
        handler.Requests[1].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].Path.Should().StartWith("/merchants/merchant-1/sdk-orders/");
        Guid.TryParse(handler.Requests[1].Path.Split('/')[^1], out _).Should().BeTrue("the probe order id is a random GUID");
        handler.Requests[1].BearerToken.Should().Be("tok");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]   // staging: "Access Forbidden!" for a merchant the keys don't belong to
    [InlineData(HttpStatusCode.BadRequest)]  // not a merchant id POK accepts
    public async Task ValidKeysButForeignMerchant_IsMerchantMismatch(HttpStatusCode merchantRead)
    {
        FakeHttpMessageHandler handler = Pok(Json(HttpStatusCode.OK, LoginOk), merchantRead);

        (await CreateSut(handler).CheckAsync("key-1", "secret-1", "other", default))
            .Should().Be(PokCredentialCheckResult.MerchantMismatch);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ValidKeysButMerchantReadUnanswered_IsUnreachable(HttpStatusCode merchantRead)
    {
        FakeHttpMessageHandler handler = Pok(Json(HttpStatusCode.OK, LoginOk), merchantRead);

        (await CreateSut(handler).CheckAsync("key-1", "secret-1", "merchant-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]  // staging: "Your credentials are incorrect..." (wrong secret or unknown key id)
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PokRefusesTheKeys_IsInvalidKeys_AndTheMerchantIsNotQueried(HttpStatusCode status)
    {
        FakeHttpMessageHandler handler = Pok(Json(status, """{"message":"Your credentials are incorrect"}"""));

        (await CreateSut(handler).CheckAsync("key-1", "wrong", "merchant-1", default))
            .Should().Be(PokCredentialCheckResult.InvalidKeys);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task LoginNotAnswered_IsUnreachable_NotInvalid(HttpStatusCode status)
    {
        FakeHttpMessageHandler handler = Pok(Json(status, "{}"));

        (await CreateSut(handler).CheckAsync("key-1", "secret-1", "merchant-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
    }

    [Fact]
    public async Task NetworkError_IsUnreachable()
    {
        FakeHttpMessageHandler handler = new(_ => throw new HttpRequestException("no route"));

        (await CreateSut(handler).CheckAsync("key-1", "secret-1", "merchant-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
    }

    [Fact]
    public async Task LoginSuccessWithoutAToken_IsUnreachable_NeverValid()
    {
        FakeHttpMessageHandler handler = Pok(Json(HttpStatusCode.OK, """{"data":{}}"""));

        (await CreateSut(handler).CheckAsync("key-1", "secret-1", "merchant-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task MerchantIdIsEscapedInThePath()
    {
        FakeHttpMessageHandler handler = Pok(Json(HttpStatusCode.OK, LoginOk), HttpStatusCode.NotFound);

        await CreateSut(handler).CheckAsync("key-1", "secret-1", "a/b?c", default);

        handler.Requests[1].RawPath.Should().StartWith("/merchants/a%2Fb%3Fc/sdk-orders/");
    }

    private sealed record CapturedRequest(string Path, string RawPath, HttpMethod Method, string Body, string? BearerToken);

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath, request.RequestUri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).Insert(0, "/"),
                request.Method, body, request.Headers.Authorization?.Parameter));
            return respond(request);
        }
    }
}
