using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.UnitTests.Money;

public class MoneyTextTests
{
    [Theory]
    [InlineData(5000, "ALL", "5,000.00 ALL")]
    [InlineData(50, "EUR", "50.00 EUR")]
    [InlineData(3000, "JPY", "3,000 JPY")]
    [InlineData(12.345, "KWD", "12.345 KWD")]
    public void Format_RendersAmountThenIsoCodeInInvariantCulture(double amount, string currency, string expected)
    {
        Assert.Equal(expected, MoneyText.Format((decimal)amount, currency));
    }

    [Fact]
    public void Format_UpperCasesTheCurrencyCode()
    {
        Assert.Equal("50.00 EUR", MoneyText.Format(50m, "eur"));
    }
}
