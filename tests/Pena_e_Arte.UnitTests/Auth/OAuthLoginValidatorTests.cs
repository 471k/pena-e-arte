using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Application.Auth.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class OAuthLoginValidatorTests
{
    private readonly OAuthLoginValidator _sut = new();

    [Theory]
    [InlineData("google")]
    [InlineData("apple")]
    public void Validate_AllowedProvider_IsValid(string provider)
    {
        ValidationResult result = _sut.Validate(Command(provider, "some-token"));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Provider");
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("")]
    public void Validate_DisallowedProvider_FailsOnProvider(string provider)
    {
        _sut.ShouldFailOn(Command(provider, "some-token"), "Request.Provider");
    }

    [Fact]
    public void Validate_EmptyIdToken_FailsOnIdToken()
    {
        _sut.ShouldFailOn(Command("google", ""), "Request.IdToken");
    }

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(Command("google", "some-token"));
    }

    private static OAuthLoginCommand Command(string provider, string idToken) =>
        new(new OAuthLoginRequest(provider, idToken));
}
