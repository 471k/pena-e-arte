using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class RefreshTokenValidatorTests
{
    private readonly RefreshTokenValidator _validator = new();

    [Fact]
    public void Validate_ValidToken_Passes()
    {
        _validator.ShouldBeValid(new RefreshTokenCommand(new RefreshTokenRequest("some.valid.token")));
    }

    [Fact]
    public void Validate_EmptyToken_FailsOnRefreshToken()
    {
        _validator.ShouldFailOn(
            new RefreshTokenCommand(new RefreshTokenRequest("")),
            "Request.RefreshToken");
    }
}
