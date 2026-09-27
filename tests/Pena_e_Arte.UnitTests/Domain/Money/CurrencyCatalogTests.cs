using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.UnitTests.Money;

public class CurrencyCatalogTests
{
    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("ALL", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("ISK", 0)]
    public void MinorUnits_ReturnsExpectedDecimalPlaces(string code, int expected)
    {
        Assert.Equal(expected, CurrencyCatalog.MinorUnits(code));
    }

    [Theory]
    [InlineData("eur", true)]
    [InlineData("XXX", false)]
    [InlineData("EURO", false)]
    public void IsSupported_IsCaseInsensitiveAndRejectsUnknownOrFundCodes(string code, bool expected)
    {
        Assert.Equal(expected, CurrencyCatalog.IsSupported(code));
    }

    [Theory]
    [InlineData(12.3456, "KWD", 12.346)]
    [InlineData(2999.5, "JPY", 3000)]
    [InlineData(10.005, "EUR", 10.01)]
    public void Round_RoundsAwayFromZeroToTheCurrencysMinorUnit(double amount, string code, double expected)
    {
        decimal result = CurrencyCatalog.Round((decimal)amount, code);
        Assert.Equal((decimal)expected, result);
    }

    [Fact]
    public void HasAtMostMinorUnits_RejectsExtraDecimalsForAZeroDecimalCurrency()
    {
        Assert.False(CurrencyCatalog.HasAtMostMinorUnits(3000.5m, "JPY"));
    }

    [Fact]
    public void HasAtMostMinorUnits_AcceptsAnExactAmount()
    {
        Assert.True(CurrencyCatalog.HasAtMostMinorUnits(12.345m, "KWD"));
    }

    [Fact]
    public void AllCodes_ExcludesNonTradableFundAndMetalCodes()
    {
        string[] excluded = ["XAU", "XDR", "XXX", "CLF", "UYW", "BOV", "CHE", "CHW", "COU", "MXV", "USN"];
        foreach (string code in excluded)
        {
            Assert.DoesNotContain(code, CurrencyCatalog.AllCodes);
        }
    }
}
