using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using Pena_e_Arte.Application.BookingDrafts.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.BookingDrafts;

public class SaveMyBookingDraftValidatorTests
{
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();

    public SaveMyBookingDraftValidatorTests() => _r2.IsR2Url(Arg.Any<string>()).Returns(c => ((string)c[0]).StartsWith("https://media.tattooos.co/"));

    private ValidationResult Validate(string payload, List<BookingDraftImageRequest>? images = null) =>
        new SaveMyBookingDraftValidator(_r2).Validate(new SaveMyBookingDraftCommand(new SaveBookingDraftRequest(payload, images)));

    [Fact]
    public void ThePayloadMayNotCarryTheSafetyNotesKey() =>
        Validate("""{"safetyNotes":"x"}""").Errors.Should().Contain(e => e.ErrorMessage == SaveMyBookingDraftHandler.ExcludedFieldMessage);

    [Fact]
    public void ThePayloadMustBeAJsonObject() =>
        Validate("[]").IsValid.Should().BeFalse();

    [Fact]
    public void PhotosMustBeStorageUrls_InAKnownCategory()
    {
        Validate("{}", [new("https://elsewhere.example/a.jpg", "AreaPhoto")]).IsValid.Should().BeFalse();
        Validate("{}", [new("https://media.tattooos.co/a.jpg", "Selfie")]).IsValid.Should().BeFalse();
        Validate("{}", [new("https://media.tattooos.co/a.jpg", "Reference")]).IsValid.Should().BeTrue();
    }
}
