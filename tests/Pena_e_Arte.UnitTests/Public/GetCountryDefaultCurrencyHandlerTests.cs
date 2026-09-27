using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetCountryDefaultCurrencyHandlerTests
{
    private readonly GetCountryDefaultCurrencyHandler _sut = new();

    [Theory]
    [InlineData("al", "AL", "ALL")]
    [InlineData("AL", "AL", "ALL")]
    [InlineData("pl", "PL", "PLN")]
    public async Task Handle_KnownCountry_ReturnsUpperCasedCodeAndDefaultCurrency(
        string input, string expectedCode, string expectedCurrency)
    {
        CountryDefaultCurrencyResponse result = await _sut.Handle(
            new GetCountryDefaultCurrencyQuery(input), default);

        result.CountryCode.Should().Be(expectedCode);
        result.Currency.Should().Be(expectedCurrency);
    }

    [Fact]
    public async Task Handle_UnknownCountry_ReturnsNullCurrencyWithoutThrowing()
    {
        CountryDefaultCurrencyResponse result = await _sut.Handle(
            new GetCountryDefaultCurrencyQuery("ZZ"), default);

        result.CountryCode.Should().Be("ZZ");
        result.Currency.Should().BeNull();
    }
}

public class GetCountryDefaultCurrencyValidatorTests
{
    private readonly GetCountryDefaultCurrencyValidator _sut = new();

    [Fact]
    public void Validate_TwoLetterCode_IsValid() =>
        _sut.ShouldBeValid(new GetCountryDefaultCurrencyQuery("AL"));

    [Fact]
    public void Validate_EmptyCode_Fails() =>
        _sut.ShouldFailOn(new GetCountryDefaultCurrencyQuery(""), "CountryCode");

    [Fact]
    public void Validate_ThreeLetterCode_Fails() =>
        _sut.ShouldFailOn(new GetCountryDefaultCurrencyQuery("ALB"), "CountryCode");

    [Fact]
    public void Validate_NumericCode_Fails() =>
        _sut.ShouldFailOn(new GetCountryDefaultCurrencyQuery("12"), "CountryCode");
}
