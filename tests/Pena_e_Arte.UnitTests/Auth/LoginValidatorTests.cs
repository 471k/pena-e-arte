using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Application.Auth.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class LoginValidatorTests
{
    private readonly LoginValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(new LoginCommand(new LoginRequest("user@example.com", "password123")));
    }

    [Fact]
    public void Validate_EmptyEmail_FailsOnEmail()
    {
        _sut.ShouldFailOn(
            new LoginCommand(new LoginRequest("", "password123")),
            "Request.Email");
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("@nodomain")]
    public void Validate_InvalidEmailFormat_FailsOnEmail(string email)
    {
        _sut.ShouldFailOn(
            new LoginCommand(new LoginRequest(email, "password123")),
            "Request.Email");
    }

    [Fact]
    public void Validate_EmptyPassword_FailsOnPassword()
    {
        _sut.ShouldFailOn(
            new LoginCommand(new LoginRequest("user@example.com", "")),
            "Request.Password");
    }

    [Fact]
    public void Validate_ValidEmailAndPassword_DoesNotFailOnPassword()
    {
        _sut.ShouldBeValid(new LoginCommand(new LoginRequest("valid@example.com", "anypass")));
    }
}
