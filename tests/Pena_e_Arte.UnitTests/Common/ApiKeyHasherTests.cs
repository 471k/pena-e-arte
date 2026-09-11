using FluentAssertions;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.UnitTests.Common;

public class ApiKeyHasherTests
{
    [Fact]
    public void GenerateNew_ReturnsKeyStartingWithItsOwnPrefix()
    {
        (string rawKey, string keyPrefix, string keyHash) = ApiKeyHasher.GenerateNew();

        rawKey.Should().StartWith(keyPrefix);
        keyHash.Should().Be(ApiKeyHasher.Hash(rawKey));
    }

    [Fact]
    public void GenerateNew_TwoCallsProduceDifferentKeys()
    {
        (string rawKey1, _, _) = ApiKeyHasher.GenerateNew();
        (string rawKey2, _, _) = ApiKeyHasher.GenerateNew();

        rawKey1.Should().NotBe(rawKey2);
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        ApiKeyHasher.Hash("same-input").Should().Be(ApiKeyHasher.Hash("same-input"));
    }

    [Fact]
    public void Hash_DifferentInputsProduceDifferentHashes()
    {
        ApiKeyHasher.Hash("input-a").Should().NotBe(ApiKeyHasher.Hash("input-b"));
    }

    [Fact]
    public void Hash_NeverEqualsTheRawKeyItself()
    {
        (string rawKey, _, string keyHash) = ApiKeyHasher.GenerateNew();

        keyHash.Should().NotBe(rawKey);
    }
}
