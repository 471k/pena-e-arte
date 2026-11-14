using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.Waitlists;

namespace Pena_e_Arte.UnitTests.Services;

public class WaitlistClaimTokensTests
{
    // One provider, as in the app: Identity's tokens and claim tokens share the key ring and differ only by purpose.
    private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();
    private readonly WaitlistClaimTokens _sut;

    // Identity's DataProtectorTokenProvider protects under its options' Name, "DataProtectorTokenProvider" by
    // default (the app calls AddDefaultTokenProviders with no rename).
    private const string IdentityTokenPurpose = "DataProtectorTokenProvider";

    public WaitlistClaimTokensTests() => _sut = new WaitlistClaimTokens(_provider);

    private static Waitlist Notified() => new()
    {
        StudioId = Guid.NewGuid(),
        Status = WaitlistStatus.Notified,
        NotifiedAt = new DateTime(2026, 11, 14, 15, 30, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Mint_ThenTryRead_RoundTripsAndMatchesTheEntry()
    {
        Waitlist entry = Notified();

        string token = _sut.Mint(entry);

        _sut.TryRead(token, out WaitlistClaimRef claim).Should().BeTrue();
        claim.EntryId.Should().Be(entry.Id);
        claim.StudioId.Should().Be(entry.StudioId);
        claim.Matches(entry).Should().BeTrue();
    }

    [Fact]
    public void Token_IsUrlSafeAndCarriesNoReadableId()
    {
        Waitlist entry = Notified();

        string token = _sut.Mint(entry);

        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        token.Should().NotContain(entry.Id.ToString("N"));
        token.Should().NotContain(entry.Id.ToString());
    }

    [Fact]
    public void Matches_IsFalseOnceTheEntryIsNotifiedAgain()
    {
        Waitlist entry = Notified();
        _sut.TryRead(_sut.Mint(entry), out WaitlistClaimRef claim);

        entry.NotifiedAt = entry.NotifiedAt!.Value.AddDays(1);

        claim.Matches(entry).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("AAAA")]
    public void TryRead_Garbage_IsFalse(string? token) =>
        _sut.TryRead(token, out _).Should().BeFalse();

    [Fact]
    public void TryRead_TamperedToken_IsFalse()
    {
        string token = _sut.Mint(Notified());
        char[] chars = token.ToCharArray();
        int middle = chars.Length / 2;
        chars[middle] = chars[middle] == 'A' ? 'B' : 'A';

        _sut.TryRead(new string(chars), out _).Should().BeFalse();
    }

    [Fact]
    public void Mint_EntryNeverNotified_Throws()
    {
        Waitlist waiting = new() { StudioId = Guid.NewGuid(), Status = WaitlistStatus.Waiting };

        Action act = () => _sut.Mint(waiting);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Purpose isolation (Desktop, 14 Nov: these two stop "one signer for everything" creeping back) ──

    [Fact]
    public void TryRead_AnIdentityTokenOnTheSameKeyRing_IsRejected()
    {
        // Even with a payload in exactly the claim format: only the purpose differs.
        Waitlist entry = Notified();
        string payload = $"{entry.Id:N}|{entry.StudioId:N}|{entry.NotifiedAt!.Value.Ticks}";
        byte[] identityProtected = _provider.CreateProtector(IdentityTokenPurpose).Protect(Encoding.UTF8.GetBytes(payload));

        _sut.TryRead(WebEncoders.Base64UrlEncode(identityProtected), out _).Should().BeFalse();
    }

    [Fact]
    public void AClaimToken_CannotBeReadAsAnIdentityToken()
    {
        string token = _sut.Mint(Notified());
        IDataProtector identityProtector = _provider.CreateProtector(IdentityTokenPurpose);

        Action asIdentityToken = () => identityProtector.Unprotect(WebEncoders.Base64UrlDecode(token));

        asIdentityToken.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Purpose_IsVersioned()
    {
        // Changing the purpose kills every link already sent; this pins it so that happens only on purpose.
        WaitlistClaimTokens.Purpose.Should().Be("Waitlist.Claim.v1");
    }
}
