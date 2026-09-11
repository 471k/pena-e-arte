using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// Covers PokPaymentProvider's own logic (credential resolution, token caching, request shaping,
/// status mapping) against a fake HTTP handler and a fake Redis — never a real POK sandbox call
/// (no staging credentials existed when this was written). See the provider's own doc comment for
/// what still needs verifying against a real staging transaction before production use.
/// </summary>
public class PokPaymentProviderTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISecretsProvider _secrets = Substitute.For<ISecretsProvider>();
    private readonly IDatabase _cache = Substitute.For<IDatabase>();
    private readonly Guid _studioId = Guid.NewGuid();
    private const string MerchantId = "merchant-abc";
    private const string SecretPath = "studios/test/pok";

    public PokPaymentProviderTests()
    {
        // No token cached and no other request holds the refresh lock, by default.
        _cache.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns((RedisValue)RedisValue.Null);
        _cache.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists, Arg.Any<CommandFlags>())
            .Returns(true);
        _cache.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.Always, Arg.Any<CommandFlags>())
            .Returns(true);
        _cache.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        _secrets.GetSecretAsync($"{SecretPath}:keyId", Arg.Any<CancellationToken>()).Returns("key-id-value");
        _secrets.GetSecretAsync($"{SecretPath}:keySecret", Arg.Any<CancellationToken>()).Returns("key-secret-value");
    }

    private PokPaymentProvider CreateSut(FakeHttpMessageHandler handler, PokOptions? options = null)
    {
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_cache);

        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Pok").Returns(_ => new HttpClient(handler));

        return new PokPaymentProvider(
            _db, _secrets,
            Options.Create(options ?? new PokOptions { BaseUrl = "https://api-staging.pokpay.io" }),
            factory, redis, NullLogger<PokPaymentProvider>.Instance);
    }

    private async Task SeedConnectedStudioAsync()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = MerchantId });
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = SecretPath,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreatePaymentHoldAsync_StudioNotConnected_ThrowsWithoutAnyHttpCall()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = null });
        await _db.SaveChangesAsync();

        FakeHttpMessageHandler handler = new(_ => throw new InvalidOperationException("Should not call POK."));
        PokPaymentProvider sut = CreateSut(handler);

        Func<Task> act = () => sut.CreatePaymentHoldAsync(
            new PaymentHoldRequest(_studioId, Guid.NewGuid(), 10000, "ALL"), default);

        await act.Should().ThrowAsync<PaymentProviderNotConnectedException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePaymentHoldAsync_HappyPath_LogsInAndCreatesOrderWithWholeUnitAmount()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok-123","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            var p when p.EndsWith("/sdk-orders") => JsonResponse(
                """{"data":{"sdkOrder":{"id":"order-abc"}}}"""),
            _ => throw new InvalidOperationException($"Unexpected call to {req.RequestUri}"),
        });
        PokPaymentProvider sut = CreateSut(handler);

        (string providerReferenceId, string clientToken) = await sut.CreatePaymentHoldAsync(
            new PaymentHoldRequest(_studioId, Guid.NewGuid(), 15000, "ALL", HoldDurationMinutes: 1440), default);

        providerReferenceId.Should().Be("order-abc");
        clientToken.Should().Be("order-abc");
        handler.Requests.Should().Contain(r => r.Path == "/auth/sdk/login");
        CapturedRequest createReq = handler.Requests.Single(r => r.Path.EndsWith("/sdk-orders"));
        createReq.Path.Should().Be($"/merchants/{MerchantId}/sdk-orders");
        // 15000 "amountInCents" → 150.00 in POK's own unit, per AmountInCentsToPok's documented
        // (unverified-against-a-real-sandbox) assumption.
        createReq.Body.Should().Contain("\"amount\":150");
        createReq.Body.Should().Contain("\"autoCapture\":false");
    }

    [Fact]
    public async Task CreatePaymentHoldAsync_TokenAlreadyCached_SkipsLogin()
    {
        await SeedConnectedStudioAsync();
        _cache.StringGetAsync($"pok:token:{_studioId}", Arg.Any<CommandFlags>())
            .Returns((RedisValue)"cached-token");

        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => throw new InvalidOperationException("Should not re-login with a cached token."),
            var p when p.EndsWith("/sdk-orders") => JsonResponse("""{"data":{"sdkOrder":{"id":"order-xyz"}}}"""),
            _ => throw new InvalidOperationException($"Unexpected call to {req.RequestUri}"),
        });
        PokPaymentProvider sut = CreateSut(handler);

        await sut.CreatePaymentHoldAsync(new PaymentHoldRequest(_studioId, Guid.NewGuid(), 1000, "ALL"), default);

        handler.Requests.Should().ContainSingle().Which.BearerToken.Should().Be("cached-token");
    }

    [Theory]
    [InlineData("""{"data":{"sdkOrder":{"isRefunded":true}}}""", PaymentProviderStatus.Refunded)]
    [InlineData("""{"data":{"sdkOrder":{"isCanceled":true}}}""", PaymentProviderStatus.Canceled)]
    [InlineData("""{"data":{"sdkOrder":{"isCompleted":true}}}""", PaymentProviderStatus.Captured)]
    [InlineData("""{"data":{"sdkOrder":{"canBeCaptured":true}}}""", PaymentProviderStatus.Authorized)]
    [InlineData("""{"data":{"sdkOrder":{}}}""", PaymentProviderStatus.Pending)]
    public async Task GetStatusAsync_MapsDocumentedFlagsToNormalizedStatus(string orderJson, PaymentProviderStatus expected)
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            _ => JsonResponse(orderJson),
        });
        PokPaymentProvider sut = CreateSut(handler);

        PaymentProviderStatus? result = await sut.GetStatusAsync(_studioId, "order-1", default);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetStatusAsync_OrderNotFound_ReturnsNull()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        PokPaymentProvider sut = CreateSut(handler);

        PaymentProviderStatus? result = await sut.GetStatusAsync(_studioId, "order-missing", default);

        result.Should().BeNull();
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>Path, method, and body captured eagerly — the SUT disposes its own
    /// HttpRequestMessage right after sending, so recording the live object (and its content)
    /// would read as disposed by the time an assertion inspects it.</summary>
    private sealed record CapturedRequest(string Path, HttpMethod Method, string Body, string? BearerToken);

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath, request.Method, body, request.Headers.Authorization?.Parameter));
            return respond(request);
        }
    }
}
