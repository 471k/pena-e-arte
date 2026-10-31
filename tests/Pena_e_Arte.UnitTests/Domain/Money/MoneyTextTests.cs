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

    [Fact]
    public void FormatWithReference_Frozen_AddsWholeLekInBrackets() =>
        Assert.Equal("50.00 EUR (4,537 ALL)", MoneyText.FormatWithReference(50m, "EUR", 4537m, "ALL", approximate: false));

    [Fact]
    public void FormatWithReference_BeforePayment_MarksTheLekApproximate() =>
        Assert.Equal("50.00 EUR (≈ 4,537 ALL)", MoneyText.FormatWithReference(50m, "EUR", 4537m, "all", approximate: true));

    [Theory]
    [InlineData(null, "ALL")]
    [InlineData(4537.0, null)]
    [InlineData(4537.0, " ")]
    public void FormatWithReference_NoReference_IsExactlyFormat(double? lek, string? currency) =>
        Assert.Equal(MoneyText.Format(50m, "EUR"),
            MoneyText.FormatWithReference(50m, "EUR", lek is null ? null : (decimal)lek, currency, approximate: true));

    [Theory]
    [InlineData(90.74, "1 EUR = 90.74 ALL, Bank of Albania official rate of 23 Oct 2026")]
    [InlineData(90.7, "1 EUR = 90.70 ALL, Bank of Albania official rate of 23 Oct 2026")]
    [InlineData(90.7425, "1 EUR = 90.7425 ALL, Bank of Albania official rate of 23 Oct 2026")]
    public void BankOfAlbaniaRateLine_StatesTheRateAsPublishedAndItsDate(double rate, string expected) =>
        Assert.Equal(expected, MoneyText.BankOfAlbaniaRateLine((decimal)rate, new DateOnly(2026, 10, 23)));
}
