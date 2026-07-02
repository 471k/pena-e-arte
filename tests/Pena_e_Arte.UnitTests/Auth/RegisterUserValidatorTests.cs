using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Application.Auth.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class RegisterUserValidatorTests
{
    private readonly RegisterUserValidator _sut = new();

    [Theory]
    [InlineData("client")]
    [InlineData("owner")]
    public void Validate_ValidRole_IsValid(string role)
    {
        ValidationResult result = _sut.Validate(Command("user@example.com", "Password1!", role, Guid.NewGuid()));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Role");
    }

    // Regression guard: this is a public [AllowAnonymous] endpoint. "artist" and "issuer"
    // must never be self-registerable here, or any caller could mint a platform-admin
    // (issuer) or attach a rogue artist account to an arbitrary studio.
    [Theory]
    [InlineData("artist")]
    [InlineData("issuer")]
    public void Validate_PrivilegedRole_FailsOnRole(string role)
    {
        _sut.ShouldFailOn(Command("u@example.com", "Password1!", role, Guid.NewGuid()), "Request.Role");
    }

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyEmail_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("", "Password1!", "owner", Guid.NewGuid()), "Request.Email");
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("bad@")]
    public void Validate_InvalidEmailFormat_FailsOnEmail(string email)
    {
        _sut.ShouldFailOn(Command(email, "Password1!", "owner", Guid.NewGuid()), "Request.Email");
    }

    [Fact]
    public void Validate_EmptyPassword_FailsOnPassword()
    {
        _sut.ShouldFailOn(Command("u@example.com", "", "owner", Guid.NewGuid()), "Request.Password");
    }

    [Fact]
    public void Validate_PasswordTooShort_FailsOnPassword()
    {
        _sut.ShouldFailOn(Command("u@example.com", "Pass1!", "owner", Guid.NewGuid()), "Request.Password");
    }

    [Fact]
    public void Validate_PasswordExactlyEightChars_DoesNotFailOnPassword()
    {
        ValidationResult result = _sut.Validate(Command("u@example.com", "Pass1234", "owner", Guid.NewGuid()));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Password");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    [InlineData("")]
    public void Validate_InvalidRole_FailsOnRole(string role)
    {
        _sut.ShouldFailOn(Command("u@example.com", "Password1!", role, Guid.NewGuid()), "Request.Role");
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("OWNER")]
    public void Validate_RoleCaseInsensitive_IsValid(string role)
    {
        ValidationResult result = _sut.Validate(Command("u@example.com", "Password1!", role, Guid.NewGuid()));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Role");
    }

    [Fact]
    public void Validate_EmptyStudioId_FailsOnStudioId()
    {
        _sut.ShouldFailOn(Command("u@example.com", "Password1!", "owner", Guid.Empty), "Request.StudioId");
    }

    private static RegisterUserCommand ValidCommand() =>
        Command("user@example.com", "Password1!", "owner", Guid.NewGuid());

    private static RegisterUserCommand Command(string email, string password, string role, Guid studioId) =>
        new(new RegisterUserRequest(email, password, role, studioId));
}
