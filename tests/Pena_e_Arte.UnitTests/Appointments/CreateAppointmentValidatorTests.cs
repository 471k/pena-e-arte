using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CreateAppointmentValidatorTests
{
    private const string ValidImageUrl = "https://cdn.example.com/appointments/ref.png";

    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly CreateAppointmentValidator _sut;

    public CreateAppointmentValidatorTests()
    {
        _r2.IsR2Url(ValidImageUrl).Returns(true);
        _sut = new CreateAppointmentValidator(_r2);
    }

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_NullArtistId_IsValid()
    {
        // Studio-choice booking — no specific artist selected — is a legitimately valid request.
        ValidationResult result = _sut.Validate(
            ValidCommand() with { Request = ValidRequest() with { ArtistId = null } });
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.ArtistId");
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { ClientId = Guid.Empty } },
            "Request.ClientId");
    }

    [Fact]
    public void Validate_PastDate_FailsOnDate()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Date = DateTime.UtcNow.AddDays(-1) } },
            "Request.Date");
    }

    [Fact]
    public void Validate_DurationBelowMinimum_FailsOnDuration()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { DurationMinutes = 29 } },
            "Request.DurationMinutes");
    }

    [Fact]
    public void Validate_DurationAboveMaximum_FailsOnDuration()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { DurationMinutes = 481 } },
            "Request.DurationMinutes");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(480)]
    public void Validate_DurationAtBoundaries_IsValid(int duration)
    {
        ValidationResult result = _sut.Validate(ValidCommand() with { Request = ValidRequest() with { DurationMinutes = duration } });
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.DurationMinutes");
    }

    [Fact]
    public void Validate_DurationInRangeButNotAnAllowedValue_FailsOnDuration()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { DurationMinutes = 100 } },
            "Request.DurationMinutes");
    }

    [Fact]
    public void Validate_NotesExceedsMaxLength_FailsOnNotes()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Notes = new string('x', 2001) } },
            "Request.Notes");
    }

    [Fact]
    public void Validate_NullNotes_IsValid()
    {
        ValidationResult result = _sut.Validate(ValidCommand() with { Request = ValidRequest() with { Notes = null } });
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }

    [Fact]
    public void Validate_ImagesWithinLimit_IsValid()
    {
        ValidationResult result = _sut.Validate(
            ValidCommand() with
            {
                Request = ValidRequest() with
                {
                    Images = [new(ValidImageUrl, "Reference"), new(ValidImageUrl, "Reference")]
                }
            });
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullImages_IsValid()
    {
        ValidationResult result = _sut.Validate(ValidCommand() with { Request = ValidRequest() with { Images = null } });
        result.Errors.Should().NotContain(e => e.PropertyName.StartsWith("Request.Images"));
    }

    [Fact]
    public void Validate_TooManyImagesInOneCategory_FailsOnImages()
    {
        List<AppointmentImageRequest> images = Enumerable.Repeat(new AppointmentImageRequest(ValidImageUrl, "Reference"), 7).ToList();

        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Images = images } },
            "Request.Images");
    }

    [Fact]
    public void Validate_ImageUrlNotFromR2_FailsOnImages()
    {
        _r2.IsR2Url("https://external.attacker.com/evil.png").Returns(false);

        _sut.ShouldFailOn(
            ValidCommand() with
            {
                Request = ValidRequest() with
                {
                    Images = [new("https://external.attacker.com/evil.png", "Reference")]
                }
            },
            "Request.Images[0].Url");
    }

    [Fact]
    public void Validate_EmptyTattooDescription_FailsOnTattooDescription()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { TattooDescription = "" } },
            "Request.TattooDescription");
    }

    [Fact]
    public void Validate_ReferralSourceOtherWithoutText_FailsOnReferralSourceOther()
    {
        _sut.ShouldFailOn(
            ValidCommand() with
            {
                Request = ValidRequest() with { ReferralSource = "Other", ReferralSourceOther = null }
            },
            "Request.ReferralSourceOther");
    }

    [Fact]
    public void Validate_ReferralSourceOtherWithText_IsValid()
    {
        ValidationResult result = _sut.Validate(
            ValidCommand() with
            {
                Request = ValidRequest() with { ReferralSource = "Other", ReferralSourceOther = "A friend told me" }
            });
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.ReferralSourceOther");
    }

    private static CreateAppointmentRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null, "A small rose on the forearm");

    private static CreateAppointmentCommand ValidCommand() =>
        new(ValidRequest());
}
