using FluentAssertions;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Platform;

/// <summary>Reproduces every cell of the yearly-refund spec's worked table (A2) plus its
/// acceptance scenarios (A10). Lives next to MrrRulesTests.cs — the prompt's suggested
/// tests/Pena_e_Arte.UnitTests/Platform/Revenue/ subfolder doesn't exist in this codebase
/// (MrrRulesTests.cs itself is directly under Platform/, not Platform/Revenue/), so this
/// file matches the actual existing convention instead of introducing a new one.</summary>
public class YearlyRefundCalculatorTests
{
    // --- A2's worked table: monthly = yearly / 10 for all four rows ---

    [Theory]
    [InlineData(290, 29, 1, 261)]   // Starter
    [InlineData(290, 29, 3, 203)]
    [InlineData(290, 29, 6, 116)]
    [InlineData(290, 29, 10, 0)]
    [InlineData(590, 59, 1, 531)]   // Growth
    [InlineData(590, 59, 3, 413)]
    [InlineData(590, 59, 6, 236)]
    [InlineData(590, 59, 10, 0)]
    [InlineData(790, 79, 1, 711)]   // Premium
    [InlineData(790, 79, 3, 553)]
    [InlineData(790, 79, 6, 316)]
    [InlineData(790, 79, 10, 0)]
    [InlineData(531, 59, 1, 472)]   // Growth, referred (10% off first-year)
    [InlineData(531, 59, 3, 354)]
    [InlineData(531, 59, 6, 177)]
    [InlineData(531, 59, 10, 0)]
    public void Compute_ReproducesA2WorkedTable(decimal amountPaid, decimal monthlyReferencePrice, int monthsUsed, decimal expectedRefund)
    {
        YearlyRefundCalculator.Compute(amountPaid, monthsUsed, monthlyReferencePrice).Should().Be(expectedRefund);
    }

    // --- A10 acceptance scenarios ---

    [Fact]
    public void MonthsUsed_BoughtJan1_CancelledMar11_Is3()
    {
        YearlyRefundCalculator.MonthsUsed(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc)).Should().Be(3);
    }

    [Fact]
    public void QuoteFor_PremiumBoughtJan1_CancelledMar11_Refund553()
    {
        SubscriptionInvoicePayment invoice = Invoice(periodStart: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), amountPaid: 790m, monthlyRef: 79m);
        YearlyRefundQuote? quote = YearlyRefundCalculator.QuoteFor(invoice, new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc));

        quote.Should().NotBeNull();
        quote!.MonthsUsed.Should().Be(3);
        quote.RefundAmount.Should().Be(553m);
    }

    [Fact]
    public void MonthsUsed_CancelledSameDayAsPurchase_Is1()
    {
        DateTime day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        YearlyRefundCalculator.MonthsUsed(day, day).Should().Be(1);
    }

    [Fact]
    public void QuoteFor_CancelledSameDayAsPurchase_Refund711()
    {
        DateTime day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SubscriptionInvoicePayment invoice = Invoice(periodStart: day, amountPaid: 790m, monthlyRef: 79m);
        YearlyRefundQuote? quote = YearlyRefundCalculator.QuoteFor(invoice, day);

        quote!.MonthsUsed.Should().Be(1);
        quote.RefundAmount.Should().Be(711m);
    }

    [Fact]
    public void MonthsUsed_BoughtJan31_CancelledFeb29LeapYear_Is2()
    {
        // 2026 is a leap year in this test's date arithmetic sense only insofar as .NET's
        // AddMonths clamping is exercised — use an actual leap year (2028) so Feb 29 exists.
        YearlyRefundCalculator.MonthsUsed(
            new DateTime(2028, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc)).Should().Be(2);
    }

    [Fact]
    public void Compute_PremiumCancelledMonth11_RefundClampedToZero()
    {
        YearlyRefundCalculator.Compute(790m, 11, 79m).Should().Be(0m);
    }

    [Fact]
    public void QuoteFor_GrowthReferredMonth3_Refund354()
    {
        SubscriptionInvoicePayment invoice = Invoice(
            periodStart: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), amountPaid: 531m, monthlyRef: 59m);
        YearlyRefundQuote? quote = YearlyRefundCalculator.QuoteFor(invoice, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));

        quote!.MonthsUsed.Should().Be(3);
        quote.RefundAmount.Should().Be(354m);
    }

    [Fact]
    public void QuoteFor_NullPeriodStart_ReturnsNull()
    {
        SubscriptionInvoicePayment invoice = Invoice(periodStart: null, amountPaid: 790m, monthlyRef: 79m);
        YearlyRefundCalculator.QuoteFor(invoice, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void QuoteFor_NullInvoice_ReturnsNull()
    {
        YearlyRefundCalculator.QuoteFor(null, DateTime.UtcNow).Should().BeNull();
    }

    private static SubscriptionInvoicePayment Invoice(DateTime? periodStart, decimal amountPaid, decimal? monthlyRef) => new()
    {
        SubscriptionId = Guid.NewGuid(),
        StudioId = Guid.NewGuid(),
        StripeInvoiceId = "in_test",
        AmountPaid = amountPaid,
        Currency = "eur",
        PaidAt = DateTime.UtcNow,
        PeriodStart = periodStart,
        MonthlyReferencePrice = monthlyRef,
    };
}
