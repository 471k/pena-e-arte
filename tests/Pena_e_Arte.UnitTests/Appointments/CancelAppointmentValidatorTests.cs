using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Validators;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CancelAppointmentValidatorTests
{
    private readonly CancelAppointmentValidator _sut = new();

    [Fact]
    public void Validate_EmptyId_FailsOnAppointmentId()
    {
        _sut.ShouldFailOn(new CancelAppointmentCommand(Guid.Empty), "AppointmentId");
    }

    [Fact]
    public void Validate_ValidId_IsValid()
    {
        _sut.ShouldBeValid(new CancelAppointmentCommand(Guid.NewGuid()));
    }
}
