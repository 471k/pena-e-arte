using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateMyStudioValidatorTests
{
    private readonly UpdateMyStudioValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(Command());
    }

    [Fact]
    public void Validate_NullNipt_IsValid()
    {
        _sut.ShouldBeValid(Command(nipt: null));
    }

    [Theory]
    [InlineData("L0123456A")]    // 9 chars
    [InlineData("L012345678A")]  // 11 chars
    [InlineData("0101234567A")]  // starts with digit
    [InlineData("L01234567")]    // missing trailing letter
    public void Validate_MalformedNipt_FailsOnNipt(string nipt)
    {
        _sut.ShouldFailOn(Command(nipt: nipt), "Request.Nipt");
    }

    [Fact]
    public void Validate_ValidNiptLowercase_IsValid()
    {
        ValidationResult result = _sut.Validate(Command(nipt: "l01234567a"));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Nipt");
    }

    [Fact]
    public void Validate_NullPhoneNumber_IsValid()
    {
        _sut.ShouldBeValid(Command(phoneNumber: null));
    }

    [Fact]
    public void Validate_ValidE164PhoneNumber_IsValid()
    {
        _sut.ShouldBeValid(Command(phoneNumber: "+351912345678"));
    }

    [Fact]
    public void Validate_NationalFormatPhoneNumberWithNoPlus_FailsOnPhoneNumber()
    {
        _sut.ShouldFailOn(Command(phoneNumber: "912345678"), "Request.PhoneNumber");
    }

    [Fact]
    public void Validate_NonPhoneShapedPhoneNumber_FailsOnPhoneNumber()
    {
        _sut.ShouldFailOn(Command(phoneNumber: "not a phone"), "Request.PhoneNumber");
    }

    private static UpdateMyStudioCommand Command(string? nipt = "L01234567A", string? phoneNumber = null) =>
        new(new UpdateStudioRequest("Studio", "Lisbon", 38.7, -9.1, PhoneNumber: phoneNumber, Nipt: nipt));
}
