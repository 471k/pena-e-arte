using FluentAssertions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Services.Social;

namespace Pena_e_Arte.UnitTests.Social;

public class SocialOAuthStateSignerTests
{
    private readonly SocialOAuthStateSigner _sut = new(
        Options.Create(new SocialSigningOptions { StateSigningKey = Convert.ToBase64String(new byte[32]) }));

    [Fact]
    public void Sign_ThenTryValidate_RoundTripsAllFields()
    {
        Guid subjectId = Guid.NewGuid();

        string state = _sut.Sign(SocialLinkSubjectType.Studio, subjectId, SocialPlatform.TikTok);

        bool valid = _sut.TryValidate(
            state, out SocialLinkSubjectType subjectType, out Guid parsedId, out SocialPlatform platform);

        valid.Should().BeTrue();
        subjectType.Should().Be(SocialLinkSubjectType.Studio);
        parsedId.Should().Be(subjectId);
        platform.Should().Be(SocialPlatform.TikTok);
    }

    [Fact]
    public void TryValidate_TamperedPayload_ReturnsFalse()
    {
        Guid subjectId = Guid.NewGuid();
        string state = _sut.Sign(SocialLinkSubjectType.Artist, subjectId, SocialPlatform.YouTube);

        // Swap the subject id in the payload but keep the original signature — must fail.
        string tampered = state.Replace(subjectId.ToString("N"), Guid.NewGuid().ToString("N"));

        bool valid = _sut.TryValidate(tampered, out _, out _, out _);

        valid.Should().BeFalse();
    }

    [Fact]
    public void TryValidate_GarbageInput_ReturnsFalseWithoutThrowing()
    {
        bool valid = _sut.TryValidate("not-a-valid-state", out _, out _, out _);

        valid.Should().BeFalse();
    }

    [Fact]
    public void TryValidate_EmptyString_ReturnsFalse()
    {
        bool valid = _sut.TryValidate("", out _, out _, out _);

        valid.Should().BeFalse();
    }

    [Fact]
    public void Sign_DifferentKeys_ProduceDifferentSignaturesForSamePayload()
    {
        SocialOAuthStateSigner other = new(
            Options.Create(new SocialSigningOptions { StateSigningKey = Convert.ToBase64String(new byte[] { 1 }.Concat(new byte[31]).ToArray()) }));

        Guid subjectId = Guid.NewGuid();
        string stateFromOther = other.Sign(SocialLinkSubjectType.Studio, subjectId, SocialPlatform.Facebook);

        bool valid = _sut.TryValidate(stateFromOther, out _, out _, out _);

        valid.Should().BeFalse();
    }
}
