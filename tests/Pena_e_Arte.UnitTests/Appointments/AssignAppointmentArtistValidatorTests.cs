using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class AssignAppointmentArtistValidatorTests
{
    private readonly AssignAppointmentArtistValidator _validator = new();

    private static AssignAppointmentArtistCommand Valid() => new(
        Guid.NewGuid(), new AssignAppointmentArtistRequest(Guid.NewGuid()));

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.ShouldBeValid(Valid());
    }

    [Fact]
    public void Validate_EmptyAppointmentId_FailsOnAppointmentId()
    {
        _validator.ShouldFailOn(
            new AssignAppointmentArtistCommand(Guid.Empty, new AssignAppointmentArtistRequest(Guid.NewGuid())),
            "AppointmentId");
    }

    [Fact]
    public void Validate_EmptyArtistId_FailsOnRequestArtistId()
    {
        _validator.ShouldFailOn(
            new AssignAppointmentArtistCommand(Guid.NewGuid(), new AssignAppointmentArtistRequest(Guid.Empty)),
            "Request.ArtistId");
    }
}
