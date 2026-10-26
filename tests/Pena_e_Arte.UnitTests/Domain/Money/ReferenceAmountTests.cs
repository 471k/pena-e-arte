using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.UnitTests.Money;

public class ReferenceAmountTests
{
    [Theory]
    [InlineData(50, 90.74, 4537)]      // 4,537.00
    [InlineData(0.50, 90.74, 45)]      // 45.37 -> 45
    [InlineData(1, 90.5, 91)]          // 90.50 -> 91: a .5 midpoint rounds away from zero
    [InlineData(3, 90.5, 272)]         // 271.50 -> 272
    [InlineData(123.45, 90.74, 11202)] // 11,201.853 -> 11,202
    [InlineData(-50, 90.74, -4537)]    // refunds shown as negatives round symmetrically
    public void ToWholeLek_ConvertsThenRoundsOnceToWholeLek(decimal eur, decimal rate, decimal expected) =>
        Assert.Equal(expected, ReferenceAmount.ToWholeLek(eur, rate));

    [Fact]
    public void ToWholeLek_NoIntermediateRounding()
    {
        // 4.995 EUR x 90.1 = 450.0495 -> 450. Rounding the euros to cents first (5.00 x 90.1 = 450.5)
        // would give 451; the exact product decides.
        Assert.Equal(450m, ReferenceAmount.ToWholeLek(4.995m, 90.1m));
    }

    [Theory]
    [InlineData("AL", "EUR", true)]
    [InlineData("al", "eur", true)]
    [InlineData("AL", "ALL", false)]
    [InlineData("XK", "EUR", false)]
    [InlineData("AL", "USD", false)]
    [InlineData("", "EUR", false)]
    public void AppliesTo_OnlyAlbanianEuroStudios(string country, string currency, bool expected)
    {
        Assert.Equal(expected, ReferenceAmount.AppliesTo(country, currency));
        Assert.Equal(expected, ReferenceAmount.AppliesTo(new Studio { CountryCode = country, Currency = currency }));
    }
}
