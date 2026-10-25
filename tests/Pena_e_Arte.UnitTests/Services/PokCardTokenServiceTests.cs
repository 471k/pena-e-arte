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
using Pena_e_Arte.Infrastructure.Services.Pok;
using Pena_e_Arte.UnitTests.Helpers;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// Covers PokCardTokenService against a fake HTTP handler and a fake Redis — never a real POK
/// call. The paths and shapes are POK's documented Card Tokenization API (see
/// IPokCardTokenService's doc comment); these tests lock them in so a change is deliberate.
/// </summary>
public class PokCardTokenServiceTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISecretsProvider _secrets = Substitute.For<ISecretsProvider>();
    private readonly IDatabase _cache = Substitute.For<IDatabase>();
    private readonly Guid _studioId = Guid.NewGuid();
    private const string MerchantId = "merchant-abc";
    private const string SecretPath = "studios/test/pok";

    private static readonly PokCardBillingInfo Billing = new(
        "Jamie", "Client", "jamie@test.com", "AL", "Tirana", "Tirana", "Rr. Myslym Shyri 10", "1001", "+355691234567");

    public PokCardTokenServiceTests()
    {
        _cache.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns((RedisValue)RedisValue.Null);
        _cache.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists, Arg.Any<CommandFlags>())
            .Returns(true);
        _cache.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.Always, Arg.Any<CommandFlags>())
            .Returns(true);
        _cache.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        _secrets.GetSecretAsync($"{SecretPath}:keyId", Arg.Any<CancellationToken>()).Returns("key-id-value");
        _secrets.GetSecretAsync($"{SecretPath}:keySecret", Arg.Any<CancellationToken>()).Returns("key-secret-value");
    }

    private PokCardTokenService CreateSut(FakeHttpMessageHandler handler)
    {
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_cache);

        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Pok").Returns(_ => new HttpClient(handler));

        PokAuthClient authClient = new(
            _db, _secrets,
            Options.Create(new PokOptions { BaseUrl = "https://api-staging.pokpay.io" }),
            factory, redis, NullLogger<PokAuthClient>.Instance);

        return new PokCardTokenService(authClient, NullLogger<PokCardTokenService>.Instance);
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
    public async Task TokenizeCardAsync_StudioNotConnected_ThrowsWithoutAnyHttpCall()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = null });
        await _db.SaveChangesAsync();

        FakeHttpMessageHandler handler = new(_ => throw new InvalidOperationException("Should not call POK."));
        PokCardTokenService sut = CreateSut(handler);

        Func<Task> act = () => sut.TokenizeCardAsync(_studioId, "jwe-value", "123", Billing, default);

        await act.Should().ThrowAsync<PaymentProviderNotConnectedException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TokenizeCardAsync_HappyPath_PostsToTokenizeGuestCardAndReadsBrandFromCardInformation()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok-123","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            "/credit-debit-cards/tokenize-guest-card" => JsonResponse(
                """{"data":{"creditDebitCard":{"id":"card-abc","holderFirstName":"Jamie","holderLastName":"Client","hiddenNumber":"411111******0023","billingInfo":{"email":"jamie@test.com"}}}}"""),
            "/credit-debit-cards/get-guest-cards-information" => JsonResponse(
                """{"data":{"cards":[{"type":"credit","brand":"visa","number":"**** **** **** 0023","label":"Visa","billingInfo":{"firstName":"Jamie"}}]}}"""),
            _ => throw new InvalidOperationException($"Unexpected call to {req.RequestUri}"),
        });
        PokCardTokenService sut = CreateSut(handler);

        PokTokenizedCard result = await sut.TokenizeCardAsync(_studioId, "jwe-value", "123", Billing, default);

        result.CardTokenId.Should().Be("card-abc");
        result.Brand.Should().Be("Visa");
        result.MaskedPan.Should().Be("**** 0023");
        result.ExpiryMonth.Should().BeNull();
        result.ExpiryYear.Should().BeNull();

        CapturedRequest tokenizeReq = handler.Requests.Single(r => r.Path == "/credit-debit-cards/tokenize-guest-card");
        tokenizeReq.Method.Should().Be(HttpMethod.Post);
        tokenizeReq.BearerToken.Should().Be("tok-123");
        tokenizeReq.Body.Should().Contain("\"csFlexCard\":{\"jwe\":\"jwe-value\"}");
        tokenizeReq.Body.Should().Contain("\"securityCode\":\"123\"");
        tokenizeReq.Body.Should().Contain("\"email\":\"jamie@test.com\"");

        CapturedRequest infoReq = handler.Requests.Single(r => r.Path == "/credit-debit-cards/get-guest-cards-information");
        infoReq.Body.Should().Be("""{"cardIds":["card-abc"]}""");
        infoReq.BearerToken.Should().Be("tok-123");
    }

    [Fact]
    public async Task TokenizeCardAsync_ResponseMissingCardId_ThrowsServiceUnavailable()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            _ => JsonResponse("""{"data":{"creditDebitCard":{}}}"""),
        });
        PokCardTokenService sut = CreateSut(handler);

        Func<Task> act = () => sut.TokenizeCardAsync(_studioId, "jwe-value", "123", Billing, default);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        handler.Requests.Should().NotContain(r => r.Path.EndsWith("/get-guest-cards-information"));
    }

    [Fact]
    public async Task TokenizeCardAsync_TokenizeRefused_ThrowsServiceUnavailable()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
        });
        PokCardTokenService sut = CreateSut(handler);

        Func<Task> act = () => sut.TokenizeCardAsync(_studioId, "jwe-value", "123", Billing, default);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
    }

    [Fact]
    public async Task TokenizeCardAsync_CardInformationFails_StillSavesWithHiddenNumberAndNoBrand()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            "/credit-debit-cards/tokenize-guest-card" => JsonResponse(
                """{"data":{"creditDebitCard":{"id":"card-1","hiddenNumber":"************1111"}}}"""),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });
        PokCardTokenService sut = CreateSut(handler);

        PokTokenizedCard result = await sut.TokenizeCardAsync(_studioId, "jwe-value", "123", Billing, default);

        result.CardTokenId.Should().Be("card-1");
        result.Brand.Should().BeNull();
        result.MaskedPan.Should().Be("**** 1111");
    }

    [Fact]
    public async Task TokenizeCardAsync_ThinResponses_DegradeToNullFieldsNotAFailure()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            "/credit-debit-cards/tokenize-guest-card" => JsonResponse("""{"data":{"creditDebitCard":{"id":"card-thin"}}}"""),
            _ => JsonResponse("""{"data":{"cards":[]}}"""),
        });
        PokCardTokenService sut = CreateSut(handler);

        PokTokenizedCard result = await sut.TokenizeCardAsync(_studioId, "jwe-value", "123", Billing, default);

        result.CardTokenId.Should().Be("card-thin");
        result.Brand.Should().BeNull();
        result.MaskedPan.Should().BeNull();
    }

    [Theory]
    [InlineData("**** **** **** 0023", "**** 0023")]
    [InlineData("411111******0023", "**** 0023")]
    [InlineData(" ************1111 ", "**** 1111")]
    [InlineData("0023", "**** 0023")]
    [InlineData("**** ****", null)]
    [InlineData("123", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FormatMaskedNumber_KeepsOnlyTheLastFourDigits(string? input, string? expected) =>
        PokCardTokenService.FormatMaskedNumber(input).Should().Be(expected);

    [Theory]
    [InlineData("visa", "Visa")]
    [InlineData("mastercard", "Mastercard")]
    [InlineData("MasterCard", "MasterCard")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void FormatBrand_TitleCasesLowercaseNames(string? input, string? expected) =>
        PokCardTokenService.FormatBrand(input).Should().Be(expected);

    [Fact]
    public async Task SetupTokenizedThreeDsAsync_HappyPath_ReturnsPayerAuthSetup()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            var p when p.EndsWith("/setup-tokenized-3ds") => JsonResponse(
                """{"data":{"payerAuthentication":{"payerAuthSetupReferenceId":"ref-1","deviceDataCollection":{"url":"https://ddc.example.com","accessToken":"ddc-token"}}}}"""),
            _ => throw new InvalidOperationException($"Unexpected call to {req.RequestUri}"),
        });
        PokCardTokenService sut = CreateSut(handler);

        PokPayerAuthSetup result = await sut.SetupTokenizedThreeDsAsync(_studioId, "order-1", "card-abc", default);

        result.PayerAuthSetupReferenceId.Should().Be("ref-1");
        result.DeviceDataCollection.Should().NotBeNull();
        result.DeviceDataCollection!.Url.Should().Be("https://ddc.example.com");
        result.DeviceDataCollection.AccessToken.Should().Be("ddc-token");

        // Root-level path, NOT under /merchants/{merchantId}/.
        CapturedRequest setupReq = handler.Requests.Single(r => r.Path.EndsWith("/setup-tokenized-3ds"));
        setupReq.Path.Should().Be("/credit-debit-cards/card-abc/setup-tokenized-3ds");
        setupReq.Body.Should().Contain("\"id\":\"order-1\"");
    }

    [Fact]
    public async Task SetupTokenizedThreeDsAsync_NoDeviceDataCollection_ReturnsNullForIt()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            _ => JsonResponse("""{"data":{"payerAuthentication":{"payerAuthSetupReferenceId":"ref-2"}}}"""),
        });
        PokCardTokenService sut = CreateSut(handler);

        PokPayerAuthSetup result = await sut.SetupTokenizedThreeDsAsync(_studioId, "order-1", "card-abc", default);

        result.PayerAuthSetupReferenceId.Should().Be("ref-2");
        result.DeviceDataCollection.Should().BeNull();
    }

    [Fact]
    public async Task SetupTokenizedThreeDsAsync_ResponseMissingReferenceId_ThrowsServiceUnavailable()
    {
        await SeedConnectedStudioAsync();
        FakeHttpMessageHandler handler = new(req => req.RequestUri!.AbsolutePath switch
        {
            "/auth/sdk/login" => JsonResponse(
                """{"data":{"accessToken":"tok","expiresAt":"2099-01-01T00:00:00Z"}}"""),
            _ => JsonResponse("""{"data":{"payerAuthentication":{}}}"""),
        });
        PokCardTokenService sut = CreateSut(handler);

        Func<Task> act = () => sut.SetupTokenizedThreeDsAsync(_studioId, "order-1", "card-abc", default);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

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
