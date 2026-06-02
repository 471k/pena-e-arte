using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CreateAppointmentValidatorTests
{
    private readonly CreateAppointmentValidator _sut = new();

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
    public void Validate_NegativeDeposit_FailsOnDepositAmount()
    {
        _sut.ShouldFailOn(
            ValidCommand() with { Request = ValidRequest() with { DepositAmount = -0.01m } },
            "Request.DepositAmount");
    }

    [Fact]
    public void Validate_ZeroDeposit_IsValid()
    {
        ValidationResult result = _sut.Validate(ValidCommand() with { Request = ValidRequest() with { DepositAmount = 0m } });
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.DepositAmount");
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

    private static CreateAppointmentRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, 50m, null);

    private static CreateAppointmentCommand ValidCommand() =>
        new(ValidRequest());
}
