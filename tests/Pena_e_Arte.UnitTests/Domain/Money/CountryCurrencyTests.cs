using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.UnitTests.Money;

public class CountryCurrencyTests
{
    [Theory]
    [InlineData("AL", "ALL")]
    [InlineData("PL", "PLN")]
    [InlineData("JP", "JPY")]
    [InlineData("XK", "EUR")]
    [InlineData("ME", "EUR")]
    [InlineData("US", "USD")]
    [InlineData("GB", "GBP")]
    public void DefaultCurrencyFor_ReturnsTheCountrysCurrency(string countryCode, string expected)
    {
        Assert.Equal(expected, CountryCurrency.DefaultCurrencyFor(countryCode));
    }

    [Fact]
    public void DefaultCurrencyFor_UnknownCountry_ReturnsNullWithoutThrowing()
    {
        Assert.Null(CountryCurrency.DefaultCurrencyFor("ZZ"));
    }

    [Theory]
    [InlineData("AL", true)]
    [InlineData("al", false)]
    [InlineData("ZZ", false)]
    [InlineData("ALB", false)]
    public void IsKnownCountry_RequiresTwoUpperCaseLettersThatRegionInfoRecognises(string code, bool expected)
    {
        Assert.Equal(expected, CountryCurrency.IsKnownCountry(code));
    }
}
