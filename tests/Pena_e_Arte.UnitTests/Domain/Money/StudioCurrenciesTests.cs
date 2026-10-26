using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.UnitTests.Money;

public class StudioCurrenciesTests
{
    [Fact]
    public void Offered_IsAllThenEur() =>
        Assert.Equal(["ALL", "EUR"], StudioCurrencies.Offered);

    [Theory]
    [InlineData("ALL", true)]
    [InlineData("EUR", true)]
    [InlineData("all", true)]
    [InlineData("eur", true)]
    [InlineData("USD", false)]
    [InlineData("ALK", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOffered_OnlyAllAndEur(string? code, bool expected) =>
        Assert.Equal(expected, StudioCurrencies.IsOffered(code));

    [Theory]
    [InlineData("AL", "ALL")]
    [InlineData("XK", "EUR")]
    [InlineData("ME", "EUR")]
    [InlineData("IT", "EUR")]
    [InlineData("US", "ALL")]
    [InlineData("JP", "ALL")]
    [InlineData("ZZ", "ALL")]
    [InlineData("", "ALL")]
    public void DefaultFor_CountryCurrencyIfOfferedElseAll(string countryCode, string expected) =>
        Assert.Equal(expected, StudioCurrencies.DefaultFor(countryCode));
}
