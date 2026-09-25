using FluentAssertions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Stripe;
using StripeLineItem = Stripe.InvoiceLineItem;

namespace Pena_e_Arte.UnitTests.Billing;

public class StripeInvoiceMapperTests
{
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 21, 48, 57, DateTimeKind.Utc);

    private static StripeLineItem Line(DateTime start, DateTime end) =>
        new() { Period = new InvoiceLineItemPeriod { Start = start, End = end } };

    [Fact]
    public void RenewalInvoice_UsesTheLinePeriod_NotTheInvoicesPreviousPeriod()
    {
        // Values from a real test-mode monthly renewal (2026-09-24/25): the invoice said
        // 09-24 -> 10-24, its line item and the subscription said 10-24 -> 11-24.
        Invoice invoice = new()
        {
            Id = "in_renewal",
            AmountPaid = 2900,
            Currency = "eur",
            PeriodStart = Utc(2026, 9, 24),
            PeriodEnd = Utc(2026, 10, 24),
            Lines = new StripeList<StripeLineItem> { Data = [Line(Utc(2026, 10, 24), Utc(2026, 11, 24))] },
        };

        StripeInvoiceInfo info = StripeInvoiceMapper.ToInfo(invoice);

        info.InvoiceId.Should().Be("in_renewal");
        info.AmountPaid.Should().Be(29m);
        info.Currency.Should().Be("eur");
        info.PeriodStart.Should().Be(Utc(2026, 10, 24));
        info.PeriodEnd.Should().Be(Utc(2026, 11, 24));
    }

    [Fact]
    public void WebhookInvoiceWithoutExpandedPayments_HasNoPaymentIntent()
    {
        // Real webhook payloads omit Invoice.payments — null here is normal, not an error.
        Invoice invoice = new()
        {
            Id = "in_webhook",
            AmountPaid = 79000,
            Currency = "eur",
            PeriodEnd = Utc(2027, 9, 24),
            Lines = new StripeList<StripeLineItem> { Data = [Line(Utc(2026, 9, 24), Utc(2027, 9, 24))] },
        };

        StripeInvoiceMapper.ToInfo(invoice).PaymentIntentId.Should().BeNull();
    }

    [Fact]
    public void CouponDiscountAndConsumedBalanceCredit_AreSummedIntoTheDiscount()
    {
        Invoice invoice = new()
        {
            Id = "in_disc",
            AmountPaid = 6900,
            Currency = "eur",
            PeriodEnd = Utc(2026, 10, 24),
            TotalDiscountAmounts = [new() { Amount = 1000 }],
            StartingBalance = -500,   // 5.00 of credit before this invoice...
            EndingBalance = 0,        // ...all consumed by it
            Lines = new StripeList<StripeLineItem> { Data = [Line(Utc(2026, 9, 24), Utc(2026, 10, 24))] },
        };

        StripeInvoiceMapper.ToInfo(invoice).DiscountAmount.Should().Be(15m); // 10.00 coupon + 5.00 credit
    }

    [Fact]
    public void RealObservedInvoice_ACreditOfFiveConsumedOnA790Invoice_IsRecordedAsFiveOfDiscount()
    {
        // Real Stripe test-mode invoice (2026-09-25): subtotal 79000, starting_balance -500,
        // ending_balance 0, amount_paid 78500, no coupon. The old formula recorded 0.00 here.
        Invoice invoice = new()
        {
            Id = "in_real",
            AmountPaid = 78500,
            Currency = "eur",
            PeriodEnd = Utc(2027, 9, 25),
            StartingBalance = -500,
            EndingBalance = 0,
            Lines = new StripeList<StripeLineItem> { Data = [Line(Utc(2026, 9, 25), Utc(2027, 9, 25))] },
        };

        StripeInvoiceMapper.ToInfo(invoice).DiscountAmount.Should().Be(5m);
    }

    [Fact]
    public void CreditOnlyPartlyConsumed_CountsOnlyWhatWasUsed()
    {
        Invoice invoice = new()
        {
            Id = "in_partial",
            AmountPaid = 0,
            Currency = "eur",
            PeriodEnd = Utc(2026, 10, 24),
            StartingBalance = -500,
            EndingBalance = -200,   // 3.00 of the 5.00 credit was used
            Lines = new StripeList<StripeLineItem> { Data = [Line(Utc(2026, 9, 24), Utc(2026, 10, 24))] },
        };

        StripeInvoiceMapper.ToInfo(invoice).DiscountAmount.Should().Be(3m);
    }

    [Fact]
    public void BalanceThatGrewOrWasNeverACredit_IsNotADiscount()
    {
        Invoice grew = new()
        {
            Id = "in_grew",
            AmountPaid = 100,
            Currency = "eur",
            PeriodEnd = Utc(2026, 10, 24),
            StartingBalance = 0,
            EndingBalance = 300,       // customer now owes more: not a credit
        };
        Invoice owed = new()
        {
            Id = "in_owed",
            AmountPaid = 100,
            Currency = "eur",
            PeriodEnd = Utc(2026, 10, 24),
            StartingBalance = 500,
            EndingBalance = 200,     // a debit shrinking: not a credit either
        };

        StripeInvoiceMapper.ToInfo(grew).DiscountAmount.Should().Be(0m);
        StripeInvoiceMapper.ToInfo(owed).DiscountAmount.Should().Be(0m);
    }

    [Fact]
    public void NoDiscountAndNoCreditConsumed_IsZero()
    {
        Invoice invoice = new()
        {
            Id = "in_plain",
            AmountPaid = 5900,
            Currency = "eur",
            PeriodEnd = Utc(2026, 10, 24),
            StartingBalance = 0,
            EndingBalance = 0,
            Lines = new StripeList<StripeLineItem> { Data = [Line(Utc(2026, 9, 24), Utc(2026, 10, 24))] },
        };

        StripeInvoiceMapper.ToInfo(invoice).DiscountAmount.Should().Be(0m);
    }

    [Fact]
    public void NoLineItems_FallsBackToTheInvoicePeriodEnd()
    {
        Invoice invoice = new() { Id = "in_nolines", AmountPaid = 100, Currency = "eur", PeriodEnd = Utc(2026, 10, 24) };

        StripeInvoiceMapper.ToInfo(invoice).PeriodEnd.Should().Be(Utc(2026, 10, 24));
    }
}
