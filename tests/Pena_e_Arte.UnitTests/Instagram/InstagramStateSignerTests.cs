using FluentAssertions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Instagram;

public class InstagramStateSignerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly MutableTimeProvider _time = new(Start);
    private readonly InstagramStateSigner _sut;

    public InstagramStateSignerTests()
    {
        _sut = new InstagramStateSigner(
            Options.Create(new InstagramOptions { TokenEncryptionKey = Convert.ToBase64String(new byte[32]) }),
            _time);
    }

    [Fact]
    public void Sign_ThenTryValidate_RoundTripsTheArtistId()
    {
        Guid artistId = Guid.NewGuid();

        bool valid = _sut.TryValidate(_sut.Sign(artistId), out Guid parsed);

        valid.Should().BeTrue();
        parsed.Should().Be(artistId);
    }

    [Fact]
    public void TryValidate_WithinTheWindow_Succeeds()
    {
        string state = _sut.Sign(Guid.NewGuid());
        _time.Advance(InstagramStateSigner.StateLifetime - TimeSpan.FromSeconds(1));

        _sut.TryValidate(state, out _).Should().BeTrue();
    }

    [Fact]
    public void TryValidate_ValidlySignedButExpired_Fails()
    {
        string state = _sut.Sign(Guid.NewGuid());
        _time.Advance(InstagramStateSigner.StateLifetime + TimeSpan.FromSeconds(1));

        _sut.TryValidate(state, out Guid artistId).Should().BeFalse();
        artistId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryValidate_StampedFarInTheFuture_Fails()
    {
        string state = _sut.Sign(Guid.NewGuid());
        _time.Advance(-TimeSpan.FromMinutes(10)); // the validating clock is now 10 minutes behind the issuer

        _sut.TryValidate(state, out _).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_SmallClockSkew_IsTolerated()
    {
        string state = _sut.Sign(Guid.NewGuid());
        _time.Advance(-TimeSpan.FromSeconds(30));

        _sut.TryValidate(state, out _).Should().BeTrue();
    }

    [Fact]
    public void TryValidate_TamperingWithTheTimestampWithoutResigning_FailsTheHmac()
    {
        Guid artistId = Guid.NewGuid();
        string state = _sut.Sign(artistId);
        long original = Start.ToUnixTimeSeconds();
        _time.Advance(InstagramStateSigner.StateLifetime + TimeSpan.FromMinutes(5));

        // Rewind the embedded timestamp to "now" to try to resurrect an expired state.
        long forged = _time.GetUtcNow().ToUnixTimeSeconds();
        string tampered = state.Replace($"|{original}.", $"|{forged}.");

        tampered.Should().NotBe(state);
        _sut.TryValidate(tampered, out _).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_TamperedArtistId_Fails()
    {
        Guid artistId = Guid.NewGuid();
        string state = _sut.Sign(artistId);

        string tampered = state.Replace(artistId.ToString("N"), Guid.NewGuid().ToString("N"));

        _sut.TryValidate(tampered, out _).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_OldFormatWithoutATimestamp_Fails()
    {
        // A pre-hardening state ("{artistId}.{sig}") must not validate: it has no issue time to bound.
        Guid artistId = Guid.NewGuid();
        string legacy = $"{artistId:N}.{Convert.ToBase64String(new byte[32])}";

        _sut.TryValidate(legacy, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-state")]
    [InlineData(".")]
    [InlineData("a|b.c")]
    public void TryValidate_GarbageInput_ReturnsFalseWithoutThrowing(string state)
    {
        _sut.TryValidate(state, out _).Should().BeFalse();
    }

    [Fact]
    public void TryValidate_StateSignedWithADifferentKey_Fails()
    {
        InstagramStateSigner other = new(
            Options.Create(new InstagramOptions { TokenEncryptionKey = Convert.ToBase64String(new byte[] { 1 }.Concat(new byte[31]).ToArray()) }),
            _time);

        _sut.TryValidate(other.Sign(Guid.NewGuid()), out _).Should().BeFalse();
    }
}
