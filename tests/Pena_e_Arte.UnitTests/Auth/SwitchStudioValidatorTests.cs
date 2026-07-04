using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class SwitchStudioValidatorTests
{
    private readonly SwitchStudioValidator _validator = new();

    [Fact]
    public void Validate_ValidStudioId_Passes()
    {
        _validator.ShouldBeValid(new SwitchStudioCommand(new SwitchStudioRequest(Guid.NewGuid())));
    }

    [Fact]
    public void Validate_EmptyStudioId_FailsOnStudioId()
    {
        _validator.ShouldFailOn(
            new SwitchStudioCommand(new SwitchStudioRequest(Guid.Empty)),
            "Request.StudioId");
    }
}
