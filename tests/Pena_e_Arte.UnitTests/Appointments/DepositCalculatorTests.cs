using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.UnitTests.Appointments;

public class DepositCalculatorTests
{
    [Fact]
    public void Calculate_NoRule_ReturnsZero() =>
        DepositCalculator.Calculate(null, 100m, 60).Should().Be(0m);

    [Fact]
    public void Calculate_FixedRule_ReturnsFixedAmount() =>
        DepositCalculator.Calculate(
            new DepositRule { AmountFixed = 50m }, 100m, 60).Should().Be(50m);

    [Fact]
    public void Calculate_FixedRule_IgnoresArtistRate() =>
        DepositCalculator.Calculate(
            new DepositRule { AmountFixed = 50m }, null, 60).Should().Be(50m);

    [Theory]
    [InlineData(20, 100, 60, 20.00)]   // 20% of 1h × €100
    [InlineData(20, 100, 90, 30.00)]   // 20% of 1.5h × €100
    [InlineData(30, 80, 60, 24.00)]    // 30% of 1h × €80
    [InlineData(25, 90, 45, 16.88)]    // 25% of 0.75h × €90 = 16.875 → rounds to 16.88
    public void Calculate_PercentRule_AppliesToHourlyRateTimesDuration(
        decimal percent, decimal rate, int minutes, decimal expected) =>
        DepositCalculator.Calculate(
            new DepositRule { AmountPercent = percent }, rate, minutes).Should().Be(expected);

    [Fact]
    public void Calculate_PercentRuleWithoutArtistRate_ReturnsZero() =>
        DepositCalculator.Calculate(
            new DepositRule { AmountPercent = 20m }, null, 60).Should().Be(0m);

    [Fact]
    public void Calculate_PercentRuleWithZeroRate_ReturnsZero() =>
        DepositCalculator.Calculate(
            new DepositRule { AmountPercent = 20m }, 0m, 60).Should().Be(0m);

    [Fact]
    public void Calculate_RuleWithBothAmounts_PrefersFixed() =>
        DepositCalculator.Calculate(
            new DepositRule { AmountFixed = 40m, AmountPercent = 20m }, 100m, 60).Should().Be(40m);
}
