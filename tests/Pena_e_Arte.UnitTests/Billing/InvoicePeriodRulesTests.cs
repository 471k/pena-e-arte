using FluentAssertions;
using Pena_e_Arte.Application.Billing;

namespace Pena_e_Arte.UnitTests.Billing;

public class InvoicePeriodRulesTests
{
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 21, 48, 57, DateTimeKind.Utc);

    [Fact]
    public void RenewalInvoice_UsesTheLineItemPeriodEnd_NotTheInvoicesPreviousPeriodEnd()
    {
        // Values from a real test-mode monthly renewal: invoice.period_end was the OLD period's end
        // (2026-10-24) while the line item, and the subscription, ran to 2026-11-24.
        DateTime result = InvoicePeriodRules.CurrentPeriodEnd(
            [Utc(2026, 11, 24)], invoicePeriodEnd: Utc(2026, 10, 24));

        result.Should().Be(Utc(2026, 11, 24));
    }

    [Fact]
    public void ProrationInvoice_WithCreditAndChargeLines_UsesTheLatestLineEnd()
    {
        DateTime result = InvoicePeriodRules.CurrentPeriodEnd(
            [Utc(2026, 10, 24), Utc(2026, 10, 24), null], invoicePeriodEnd: Utc(2026, 9, 24));

        result.Should().Be(Utc(2026, 10, 24));
    }

    [Fact]
    public void NoLinePeriods_FallsBackToTheInvoicePeriodEnd()
    {
        InvoicePeriodRules.CurrentPeriodEnd([], Utc(2026, 10, 24)).Should().Be(Utc(2026, 10, 24));
        InvoicePeriodRules.CurrentPeriodEnd([null, null], Utc(2026, 10, 24)).Should().Be(Utc(2026, 10, 24));
    }
}
