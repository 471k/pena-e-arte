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

/// <summary>C-7: the owner's POK keys are tested with one login before they are saved.</summary>
public class PokCredentialVerifierTests
{
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

    [Fact]
    public async Task CheckKeysAsync_LoginReturnsAToken_IsValid_AndOnlyCallsLogin()
    {
        FakeHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK,
            """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""));

        PokCredentialCheckResult result = await CreateSut(handler).CheckKeysAsync("key-1", "secret-1", default);

        result.Should().Be(PokCredentialCheckResult.Valid);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("/auth/sdk/login");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Body.Should().Contain("\"keyId\":\"key-1\"").And.Contain("\"keySecret\":\"secret-1\"");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task CheckKeysAsync_PokRefusesTheKeys_IsInvalidKeys(HttpStatusCode status)
    {
        FakeHttpMessageHandler handler = new(_ => Json(status, """{"message":"Invalid credentials"}"""));

        (await CreateSut(handler).CheckKeysAsync("key-1", "wrong", default))
            .Should().Be(PokCredentialCheckResult.InvalidKeys);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task CheckKeysAsync_PokNotAnswering_IsUnreachable_NotInvalid(HttpStatusCode status)
    {
        FakeHttpMessageHandler handler = new(_ => Json(status, "{}"));

        (await CreateSut(handler).CheckKeysAsync("key-1", "secret-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
    }

    [Fact]
    public async Task CheckKeysAsync_NetworkError_IsUnreachable()
    {
        FakeHttpMessageHandler handler = new(_ => throw new HttpRequestException("no route"));

        (await CreateSut(handler).CheckKeysAsync("key-1", "secret-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
    }

    [Fact]
    public async Task CheckKeysAsync_SuccessWithoutAToken_IsUnreachable_NeverValid()
    {
        FakeHttpMessageHandler handler = new(_ => Json(HttpStatusCode.OK, """{"data":{}}"""));

        (await CreateSut(handler).CheckKeysAsync("key-1", "secret-1", default))
            .Should().Be(PokCredentialCheckResult.PokUnreachable);
    }

    private sealed record CapturedRequest(string Path, HttpMethod Method, string Body);

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new CapturedRequest(request.RequestUri!.AbsolutePath, request.Method, body));
            return respond(request);
        }
    }
}
