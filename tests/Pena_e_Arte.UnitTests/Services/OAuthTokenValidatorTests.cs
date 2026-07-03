using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Services;

public class OAuthTokenValidatorTests : IDisposable
{
    private const string GoogleIssuer = "https://accounts.google.com";
    private const string GoogleAudience = "test-google-client-id";
    private const string KeyId = "test-key-1";

    private readonly RSA                _rsa    = RSA.Create(2048);
    private readonly IHttpClientFactory _http   = Substitute.For<IHttpClientFactory>();
    private readonly IDistributedCache  _cache  = new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions()));

    public void Dispose() => _rsa.Dispose();

    private OAuthTokenValidator CreateSut() => new(
        _http,
        _cache,
        Options.Create(new GoogleOptions { ClientId = GoogleAudience }),
        Options.Create(new AppleOptions { ClientId = "test-apple-client-id" }),
        NullLogger<OAuthTokenValidator>.Instance);

    private string BuildJwksJson()
    {
        RSAParameters publicParams = _rsa.ExportParameters(false);
        string n = Base64UrlEncoder.Encode(publicParams.Modulus);
        string e = Base64UrlEncoder.Encode(publicParams.Exponent);
        return $$"""{"keys":[{"kty":"RSA","kid":"{{KeyId}}","use":"sig","alg":"RS256","n":"{{n}}","e":"{{e}}"}]}""";
    }

    private string BuildSignedGoogleToken(string email = "user@example.com", string sub = "sub-123")
    {
        RsaSecurityKey key = new(_rsa) { KeyId = KeyId };
        SigningCredentials creds = new(key, SecurityAlgorithms.RsaSha256);

        JwtSecurityToken token = new(
            issuer:             GoogleIssuer,
            audience:           GoogleAudience,
            claims:             [new Claim("email", email), new Claim("sub", sub)],
            expires:            DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void MockHttpReturnsJwks()
    {
        string jwksJson = BuildJwksJson();
        FakeHttpMessageHandler handler = new(jwksJson);
        _http.CreateClient("OAuthJwks").Returns(new HttpClient(handler));
    }

    [Fact]
    public async Task ValidateGoogleTokenAsync_CacheMiss_FetchesFromHttpAndCaches()
    {
        MockHttpReturnsJwks();
        string idToken = BuildSignedGoogleToken();

        OAuthUserInfo result = await CreateSut().ValidateGoogleTokenAsync(idToken, default);

        result.Email.Should().Be("user@example.com");
        result.ProviderUserId.Should().Be("sub-123");
        (await _cache.GetAsync("jwks_google")).Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateGoogleTokenAsync_SecondCall_HitsCacheNotHttp()
    {
        MockHttpReturnsJwks();
        string idToken = BuildSignedGoogleToken();
        OAuthTokenValidator sut = CreateSut();

        await sut.ValidateGoogleTokenAsync(idToken, default);
        await sut.ValidateGoogleTokenAsync(idToken, default);

        _http.Received(1).CreateClient("OAuthJwks");
    }

    [Fact]
    public async Task ValidateGoogleTokenAsync_TokenSignedWithWrongKey_ThrowsInvalidOperationException()
    {
        MockHttpReturnsJwks();

        using RSA otherRsa = RSA.Create(2048);
        RsaSecurityKey otherKey = new(otherRsa) { KeyId = KeyId };
        SigningCredentials badCreds = new(otherKey, SecurityAlgorithms.RsaSha256);
        JwtSecurityToken badToken = new(
            issuer: GoogleIssuer, audience: GoogleAudience,
            claims: [new Claim("email", "user@example.com"), new Claim("sub", "sub-123")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: badCreds);
        string idToken = new JwtSecurityTokenHandler().WriteToken(badToken);

        Func<Task> act = () => CreateSut().ValidateGoogleTokenAsync(idToken, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
    }
}
