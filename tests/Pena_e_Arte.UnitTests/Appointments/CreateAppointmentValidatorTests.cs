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
    public void Validate_EmptyArtistId_FailsOnArtistId()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { ArtistId = Guid.Empty } },
            "Request.ArtistId");
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
    public void Validate_ImageUrlsWithinLimit_IsValid()
    {
        ValidationResult result = _sut.Validate(
            ValidCommand() with { Request = ValidRequest() with { ImageUrls = [ValidImageUrl, ValidImageUrl] } });
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullImageUrls_IsValid()
    {
        ValidationResult result = _sut.Validate(ValidCommand() with { Request = ValidRequest() with { ImageUrls = null } });
        result.Errors.Should().NotContain(e => e.PropertyName.StartsWith("Request.ImageUrls"));
    }

    [Fact]
    public void Validate_TooManyImageUrls_FailsOnImageUrls()
    {
        List<string> urls = Enumerable.Repeat(ValidImageUrl, 7).ToList();

        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { ImageUrls = urls } },
            "Request.ImageUrls");
    }

    [Fact]
    public void Validate_ImageUrlNotFromR2_FailsOnImageUrls()
    {
        _r2.IsR2Url("https://external.attacker.com/evil.png").Returns(false);

        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { ImageUrls = ["https://external.attacker.com/evil.png"] } },
            "Request.ImageUrls[0]");
    }

    private static CreateAppointmentRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);

    private static CreateAppointmentCommand ValidCommand() =>
        new(ValidRequest());
}
