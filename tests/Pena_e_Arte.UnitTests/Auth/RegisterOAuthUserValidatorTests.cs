using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Application.Auth.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class RegisterOAuthUserValidatorTests
{
    private readonly RegisterOAuthUserValidator _sut = new();

    [Theory]
    [InlineData("client")]
    [InlineData("owner")]
    public void Validate_ValidRole_IsValid(string role)
    {
        ValidationResult result = _sut.Validate(Command("google", "token", role, Guid.NewGuid()));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Role");
    }

    // Same regression guard as RegisterUserValidatorTests: this is a public
    // [AllowAnonymous] endpoint. "artist" and "admin" must never be self-registerable
    // here via OAuth either.
    [Theory]
    [InlineData("artist")]
    [InlineData("admin")]
    public void Validate_PrivilegedRole_FailsOnRole(string role)
    {
        _sut.ShouldFailOn(Command("google", "token", role, Guid.NewGuid()), "Request.Role");
    }

    [Fact]
    public void Validate_EmptyStudioId_FailsOnStudioId()
    {
        _sut.ShouldFailOn(Command("google", "token", "owner", Guid.Empty), "Request.StudioId");
    }

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(Command("google", "token", "owner", Guid.NewGuid()));
    }

    private static RegisterOAuthUserCommand Command(string provider, string idToken, string role, Guid studioId) =>
        new(new RegisterOAuthUserRequest(provider, idToken, role, studioId));
}
