using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class RescheduleAppointmentValidatorTests
{
    private readonly RescheduleAppointmentValidator _validator = new();

    private static RescheduleAppointmentCommand Valid() => new(
        Guid.NewGuid(),
        new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(2), 60, null));

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.ShouldBeValid(Valid());
    }

    [Fact]
    public void Validate_EmptyAppointmentId_FailsOnAppointmentId()
    {
        _validator.ShouldFailOn(
            new RescheduleAppointmentCommand(Guid.Empty,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(2), 60, null)),
            "AppointmentId");
    }

    [Fact]
    public void Validate_PastDate_FailsOnNewDate()
    {
        _validator.ShouldFailOn(
            new RescheduleAppointmentCommand(Guid.NewGuid(),
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(-1), 60, null)),
            "Request.NewDate");
    }

    [Fact]
    public void Validate_DurationTooShort_FailsOnNewDurationMinutes()
    {
        _validator.ShouldFailOn(
            new RescheduleAppointmentCommand(Guid.NewGuid(),
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), 20, null)),
            "Request.NewDurationMinutes");
    }

    [Fact]
    public void Validate_DurationTooLong_FailsOnNewDurationMinutes()
    {
        _validator.ShouldFailOn(
            new RescheduleAppointmentCommand(Guid.NewGuid(),
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), 500, null)),
            "Request.NewDurationMinutes");
    }

    [Fact]
    public void Validate_DurationInRangeButNotInDiscreteSet_FailsOnNewDurationMinutes()
    {
        // 100 is within the old 30-480 inclusive range but isn't one of the discrete
        // session lengths CreateAppointmentValidator/BookAppointmentForm offer —
        // rescheduling must not be a way to set a duration a new booking never could.
        _validator.ShouldFailOn(
            new RescheduleAppointmentCommand(Guid.NewGuid(),
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), 100, null)),
            "Request.NewDurationMinutes");
    }
}
