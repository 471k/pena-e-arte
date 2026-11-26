using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.BookingDrafts;
using Pena_e_Arte.Infrastructure.Services.Waitlists;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>C05: the resume token copies the waitlist claim token's pattern.</summary>
public class BookingDraftResumeTokensTests
{
    // One provider, as in the app: every purpose shares the key ring and differs only by purpose string.
    private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();
    private readonly BookingDraftResumeTokens _sut;

    public BookingDraftResumeTokensTests() => _sut = new BookingDraftResumeTokens(_provider);

    private static BookingDraft Draft() => new()
    {
        StudioId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        CreatedAt = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Mint_ThenTryRead_RoundTripsAndMatchesTheDraft()
    {
        BookingDraft draft = Draft();

        string token = _sut.Mint(draft);

        _sut.TryRead(token, out BookingDraftResumeRef resume).Should().BeTrue();
        resume.DraftId.Should().Be(draft.Id);
        resume.StudioId.Should().Be(draft.StudioId);
        resume.Matches(draft).Should().BeTrue();
        resume.ExpiresAt.Should().Be(draft.CreatedAt + BookingDraft.Lifetime);
        token.Should().MatchRegex("^[A-Za-z0-9_-]+$").And.NotContain(draft.Id.ToString("N"));
    }

    [Fact]
    public void Matches_IsFalseForADifferentWrite()
    {
        BookingDraft draft = Draft();
        _sut.TryRead(_sut.Mint(draft), out BookingDraftResumeRef resume);

        BookingDraft replaced = new() { StudioId = draft.StudioId, UserId = draft.UserId, CreatedAt = draft.CreatedAt };
        resume.Matches(replaced).Should().BeFalse("a replacing write is a new row with a new id");
    }

    [Fact]
    public void TryRead_ATokenMintedForAnotherPurpose_Fails()
    {
        // A waitlist claim token from the same key ring: valid Data Protection output, wrong purpose.
        Waitlist entry = new() { StudioId = Guid.NewGuid(), NotifiedAt = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc) };
        string waitlistToken = new WaitlistClaimTokens(_provider).Mint(entry);

        _sut.TryRead(waitlistToken, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_AnOverLongInput_Fails()
    {
        string token = _sut.Mint(Draft());

        _sut.TryRead(token + new string('A', 2049), out _).Should().BeFalse();
        _sut.TryRead(new string('A', 2049), out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_ATamperedToken_Fails()
    {
        byte[] bytes = WebEncoders.Base64UrlDecode(_sut.Mint(Draft()));
        bytes[^1] ^= 0x01;

        _sut.TryRead(WebEncoders.Base64UrlEncode(bytes), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !")]
    public void TryRead_Garbage_Fails(string? token) => _sut.TryRead(token, out _).Should().BeFalse();

    [Fact]
    public void TryRead_AWellProtectedPayloadOfTheWrongShape_Fails()
    {
        IDataProtector protector = _provider.CreateProtector(BookingDraftResumeTokens.Purpose);
        string token = WebEncoders.Base64UrlEncode(protector.Protect(Encoding.UTF8.GetBytes("only|two")));

        _sut.TryRead(token, out _).Should().BeFalse();
    }

    [Fact]
    public void Mint_RefusesACreatedAtWithSubSecondTicks()
    {
        BookingDraft draft = new() { StudioId = Guid.NewGuid(), CreatedAt = new DateTime(2026, 11, 25, 12, 0, 0, 5, DateTimeKind.Utc) };

        Action act = () => _sut.Mint(draft);

        act.Should().Throw<InvalidOperationException>();
    }
}
