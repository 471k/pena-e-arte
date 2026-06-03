using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class ConnectStudioValidatorTests
{
    private readonly ConnectStudioValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyReturnUrl_FailsOnReturnUrl()
    {
        _sut.ShouldFailOn(Command("", "https://example.com/refresh", "PT"), "Request.ReturnUrl");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp-not-http")]
    [InlineData("just text")]
    public void Validate_RelativeOrInvalidReturnUrl_FailsOnReturnUrl(string url)
    {
        _sut.ShouldFailOn(Command(url, "https://example.com/refresh", "PT"), "Request.ReturnUrl");
    }

    [Fact]
    public void Validate_EmptyRefreshUrl_FailsOnRefreshUrl()
    {
        _sut.ShouldFailOn(Command("https://example.com/return", "", "PT"), "Request.RefreshUrl");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative-path")]
    public void Validate_RelativeOrInvalidRefreshUrl_FailsOnRefreshUrl(string url)
    {
        _sut.ShouldFailOn(Command("https://example.com/return", url, "PT"), "Request.RefreshUrl");
    }

    [Fact]
    public void Validate_EmptyCountry_FailsOnCountry()
    {
        _sut.ShouldFailOn(Command("https://example.com/return", "https://example.com/refresh", ""), "Request.Country");
    }

    [Theory]
    [InlineData("P")]
    [InlineData("PRT")]
    [InlineData("12")]
    public void Validate_CountryWrongLengthOrFormat_FailsOnCountry(string country)
    {
        _sut.ShouldFailOn(Command("https://example.com/return", "https://example.com/refresh", country), "Request.Country");
    }

    [Theory]
    [InlineData("PT")]
    [InlineData("US")]
    [InlineData("pt")]
    [InlineData("gb")]
    public void Validate_ValidCountryCode_DoesNotFailOnCountry(string country)
    {
        ValidationResult result = _sut.Validate(Command("https://example.com/return", "https://example.com/refresh", country));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Country");
    }

    private static ConnectStudioCommand ValidCommand() =>
        Command("https://example.com/return", "https://example.com/refresh", "PT");

    private static ConnectStudioCommand Command(string returnUrl, string refreshUrl, string country) =>
        new(new ConnectStudioRequest(returnUrl, refreshUrl, country));
}
