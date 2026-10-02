using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Instagram;

public class MetaSignedRequestParserTests
{
    private const string Secret = "test-app-secret";

    private static MetaSignedRequestParser CreateSut(string secret = Secret) =>
        new(Options.Create(new InstagramOptions { AppSecret = secret }));

    // Builds a request exactly the way Meta does: HMAC-SHA256 over the still-encoded payload.
    private static string Sign(string payloadJson, string secret = Secret)
    {
        string encodedPayload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        byte[] signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(encodedPayload));
        return $"{Base64Url.EncodeToString(signature)}.{encodedPayload}";
    }

    [Fact]
    public void TryGetUserId_ValidRequestWithStringUserId_ReturnsTheId()
    {
        string request = Sign("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"17841400000000000\"}");

        bool ok = CreateSut().TryGetUserId(request, out string userId);

        ok.Should().BeTrue();
        userId.Should().Be("17841400000000000");
    }

    [Fact]
    public void TryGetUserId_ValidRequestWithNumericUserId_ReturnsTheIdAsText()
    {
        string request = Sign("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":17841400000000000}");

        bool ok = CreateSut().TryGetUserId(request, out string userId);

        ok.Should().BeTrue();
        userId.Should().Be("17841400000000000");
    }

    [Fact]
    public void TryGetUserId_SignedWithADifferentSecret_IsRejected()
    {
        string request = Sign("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"1\"}", secret: "someone-elses-secret");

        CreateSut().TryGetUserId(request, out string userId).Should().BeFalse();
        userId.Should().BeEmpty();
    }

    [Fact]
    public void TryGetUserId_PayloadSwappedAfterSigning_IsRejected()
    {
        string genuine = Sign("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"1\"}");
        string forgedPayload = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"2\"}"));
        string tampered = genuine[..genuine.IndexOf('.')] + "." + forgedPayload;

        CreateSut().TryGetUserId(tampered, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetUserId_UnsupportedAlgorithm_IsRejectedEvenWhenSignatureMatches()
    {
        string request = Sign("{\"algorithm\":\"none\",\"user_id\":\"1\"}");

        CreateSut().TryGetUserId(request, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetUserId_MissingOrEmptyUserId_IsRejected()
    {
        CreateSut().TryGetUserId(Sign("{\"algorithm\":\"HMAC-SHA256\"}"), out _).Should().BeFalse();
        CreateSut().TryGetUserId(Sign("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"\"}"), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-dot-here")]
    [InlineData(".payload-only")]
    [InlineData("signature-only.")]
    [InlineData("%%%.%%%")]
    public void TryGetUserId_MalformedInput_IsRejectedWithoutThrowing(string request)
    {
        CreateSut().TryGetUserId(request, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetUserId_NoAppSecretConfigured_FailsClosed()
    {
        // An empty secret would let anyone forge a valid signature, so nothing may verify.
        string request = Sign("{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"1\"}", secret: "");

        CreateSut(secret: "").TryGetUserId(request, out _).Should().BeFalse();
    }
}
