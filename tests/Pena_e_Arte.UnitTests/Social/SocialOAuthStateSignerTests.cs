using FluentAssertions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Services.Social;
using Pena_e_Arte.UnitTests.Helpers;

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

    private static readonly DateTimeOffset Start = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static SocialOAuthStateSigner CreateWithClock(MutableTimeProvider time) => new(
        Options.Create(new SocialSigningOptions { StateSigningKey = Convert.ToBase64String(new byte[32]) }), time);

    [Fact]
    public void TryValidate_WithinTheWindow_Succeeds()
    {
        MutableTimeProvider time = new(Start);
        SocialOAuthStateSigner sut = CreateWithClock(time);
        string state = sut.Sign(SocialLinkSubjectType.Artist, Guid.NewGuid(), SocialPlatform.TikTok);

        time.Advance(SocialOAuthStateSigner.StateLifetime - TimeSpan.FromSeconds(1));

        sut.TryValidate(state, out _, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void TryValidate_ValidlySignedButExpired_Fails()
    {
        MutableTimeProvider time = new(Start);
        SocialOAuthStateSigner sut = CreateWithClock(time);
        string state = sut.Sign(SocialLinkSubjectType.Artist, Guid.NewGuid(), SocialPlatform.TikTok);

        time.Advance(SocialOAuthStateSigner.StateLifetime + TimeSpan.FromSeconds(1));

        sut.TryValidate(state, out SocialLinkSubjectType type, out Guid id, out SocialPlatform platform).Should().BeFalse();
        (type, id, platform).Should().Be((default(SocialLinkSubjectType), Guid.Empty, default(SocialPlatform)));
    }

    [Fact]
    public void TryValidate_TamperingWithTheTimestampWithoutResigning_FailsTheHmac()
    {
        MutableTimeProvider time = new(Start);
        SocialOAuthStateSigner sut = CreateWithClock(time);
        string state = sut.Sign(SocialLinkSubjectType.Studio, Guid.NewGuid(), SocialPlatform.Facebook);
        long original = Start.ToUnixTimeSeconds();

        time.Advance(SocialOAuthStateSigner.StateLifetime + TimeSpan.FromMinutes(5));
        string tampered = state.Replace($"|{original}.", $"|{time.GetUtcNow().ToUnixTimeSeconds()}.");

        tampered.Should().NotBe(state);
        sut.TryValidate(tampered, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_StampedFarInTheFuture_Fails()
    {
        MutableTimeProvider time = new(Start);
        SocialOAuthStateSigner sut = CreateWithClock(time);
        string state = sut.Sign(SocialLinkSubjectType.Artist, Guid.NewGuid(), SocialPlatform.X);

        time.Advance(-TimeSpan.FromMinutes(10));

        sut.TryValidate(state, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_OldFormatWithoutATimestamp_Fails()
    {
        SocialOAuthStateSigner sut = CreateWithClock(new MutableTimeProvider(Start));
        string legacy = $"Artist|{Guid.NewGuid():N}|TikTok.{Convert.ToBase64String(new byte[32])}";

        sut.TryValidate(legacy, out _, out _, out _).Should().BeFalse();
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
