using FluentAssertions;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.UnitTests.Common;

public class WebhookSignerTests
{
    [Fact]
    public void Sign_SameInputs_ProducesDeterministicSignature()
    {
        string sig1 = WebhookSigner.Sign("secret", "1700000000", "{\"a\":1}");
        string sig2 = WebhookSigner.Sign("secret", "1700000000", "{\"a\":1}");

        sig1.Should().Be(sig2);
    }

    [Fact]
    public void Sign_DifferentBody_ProducesDifferentSignature()
    {
        string sig1 = WebhookSigner.Sign("secret", "1700000000", "{\"a\":1}");
        string sig2 = WebhookSigner.Sign("secret", "1700000000", "{\"a\":2}");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void Sign_DifferentSecret_ProducesDifferentSignature()
    {
        string sig1 = WebhookSigner.Sign("secret-a", "1700000000", "{\"a\":1}");
        string sig2 = WebhookSigner.Sign("secret-b", "1700000000", "{\"a\":1}");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void Sign_DifferentTimestamp_ProducesDifferentSignature()
    {
        string sig1 = WebhookSigner.Sign("secret", "1700000000", "{\"a\":1}");
        string sig2 = WebhookSigner.Sign("secret", "1700000001", "{\"a\":1}");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void Sign_ProducesLowercaseHex()
    {
        string sig = WebhookSigner.Sign("secret", "1700000000", "{}");

        sig.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void GenerateSecret_HasExpectedPrefixAndIsUnique()
    {
        string secret1 = WebhookSigner.GenerateSecret();
        string secret2 = WebhookSigner.GenerateSecret();

        secret1.Should().StartWith("whsec_");
        secret1.Should().NotBe(secret2);
    }
}
