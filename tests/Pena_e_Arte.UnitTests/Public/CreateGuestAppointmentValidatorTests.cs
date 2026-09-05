using FluentValidation.Results;
using NSubstitute;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Application.Public.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;
using FluentAssertions;

namespace Pena_e_Arte.UnitTests.Public;

public class CreateGuestAppointmentValidatorTests
{
    private const string ValidImageUrl = "https://cdn.example.com/appointments/guest-pending/ref.png";

    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly CreateGuestAppointmentValidator _sut;

    public CreateGuestAppointmentValidatorTests()
    {
        _r2.IsR2Url(ValidImageUrl).Returns(true);
        _sut = new CreateGuestAppointmentValidator(_r2);
    }

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyFirstName_FailsOnFirstName()
    {
        _sut.ShouldFailOn(ValidCommand() with { Request = ValidRequest() with { FirstName = "" } }, "Request.FirstName");
    }

    [Fact]
    public void Validate_EmptyLastName_FailsOnLastName()
    {
        _sut.ShouldFailOn(ValidCommand() with { Request = ValidRequest() with { LastName = "" } }, "Request.LastName");
    }

    [Fact]
    public void Validate_InvalidEmail_FailsOnEmail()
    {
        _sut.ShouldFailOn(ValidCommand() with { Request = ValidRequest() with { Email = "not-an-email" } }, "Request.Email");
    }

    [Fact]
    public void Validate_EmptyPhone_FailsOnPhone()
    {
        _sut.ShouldFailOn(ValidCommand() with { Request = ValidRequest() with { Phone = "" } }, "Request.Phone");
    }

    [Theory]
    [InlineData("912345678")]      // missing '+' and country code
    [InlineData("+0123456789")]    // leading zero after '+' is not a valid calling code
    [InlineData("not-a-phone")]
    public void Validate_NonE164Phone_FailsOnPhone(string phone)
    {
        _sut.ShouldFailOn(ValidCommand() with { Request = ValidRequest() with { Phone = phone } }, "Request.Phone");
    }

    [Fact]
    public void Validate_ValidE164Phone_IsValid()
    {
        ValidationResult result = _sut.Validate(ValidCommand() with { Request = ValidRequest() with { Phone = "+351912345678" } });
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Phone");
    }

    [Fact]
    public void Validate_EmptyTattooDescription_FailsOnTattooDescription()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Booking = ValidBooking() with { TattooDescription = "" } } },
            "Request.Booking.TattooDescription");
    }

    [Fact]
    public void Validate_MissingAreaPhoto_FailsOnImages()
    {
        _sut.ShouldFailOn(
            ValidCommand() with
            {
                Request = ValidRequest() with
                {
                    Booking = ValidBooking() with { Images = [new(ValidImageUrl, "Reference")] }
                }
            },
            "Request.Booking.Images");
    }

    [Fact]
    public void Validate_MissingReferenceImage_FailsOnImages()
    {
        _sut.ShouldFailOn(
            ValidCommand() with
            {
                Request = ValidRequest() with
                {
                    Booking = ValidBooking() with { Images = [new(ValidImageUrl, "AreaPhoto")] }
                }
            },
            "Request.Booking.Images");
    }

    [Fact]
    public void Validate_BothCategoriesPresent_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_ReferralSourceOtherWithoutText_FailsOnReferralSourceOther()
    {
        _sut.ShouldFailOn(
            ValidCommand() with
            {
                Request = ValidRequest() with
                {
                    Booking = ValidBooking() with { ReferralSource = "Other", ReferralSourceOther = null }
                }
            },
            "Request.Booking.ReferralSourceOther");
    }

    [Fact]
    public void Validate_InvalidReferralSource_FailsOnReferralSource()
    {
        _sut.ShouldFailOn(
            ValidCommand() with
            {
                Request = ValidRequest() with { Booking = ValidBooking() with { ReferralSource = "Carrier Pigeon" } }
            },
            "Request.Booking.ReferralSource");
    }

    [Fact]
    public void Validate_TooManyImagesInOneCategory_FailsOnImages()
    {
        List<AppointmentImageRequest> images =
        [
            new(ValidImageUrl, "AreaPhoto"),
            .. Enumerable.Repeat(new AppointmentImageRequest(ValidImageUrl, "Reference"), 7),
        ];

        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Booking = ValidBooking() with { Images = images } } },
            "Request.Booking.Images");
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
                    Booking = ValidBooking() with
                    {
                        Images = [new("https://external.attacker.com/evil.png", "AreaPhoto"), new(ValidImageUrl, "Reference")]
                    }
                }
            },
            "Request.Booking.Images[0].Url");
    }

    [Fact]
    public void Validate_PastDate_FailsOnDate()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Booking = ValidBooking() with { Date = DateTime.UtcNow.AddDays(-1) } } },
            "Request.Booking.Date");
    }

    [Fact]
    public void Validate_InvalidDuration_FailsOnDuration()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { Booking = ValidBooking() with { DurationMinutes = 100 } } },
            "Request.Booking.DurationMinutes");
    }

    private static CreateAppointmentRequest ValidBooking() => new(
        Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(3), 90, null,
        "A small rose on the forearm",
        DesiredPlacementLocations: ["forearm_left"],
        Images: [new(ValidImageUrl, "AreaPhoto"), new(ValidImageUrl, "Reference")]);

    private static CreateGuestAppointmentRequest ValidRequest() => new(
        "Jamie", "Guest", "jamie@example.com", "+351912345678", MarketingOptIn: true, Booking: ValidBooking());

    private static CreateGuestAppointmentCommand ValidCommand() => new("guest-studio", ValidRequest());
}
