using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// The one place a Stripe <see cref="Invoice"/> becomes the facts HandleInvoicePaidCommand records —
/// shared by the invoice.paid webhook and the "record the first invoice at activation" path, so the
/// discount composition and period handling can never drift between them.
/// </summary>
public static class StripeInvoiceMapper
{
    public static StripeInvoiceInfo ToInfo(Invoice invoice)
    {
        decimal discountAmount = (invoice.TotalDiscountAmounts?.Sum(d => d.Amount) ?? 0) / 100m;

        // A customer-balance credit consumed on this invoice also counts as a discount (R3, the
        // Yearly-referrer case). Stripe balances are negative for credit, and applying credit moves the
        // balance TOWARD zero, so the credit consumed is (ending - starting) while the starting balance
        // is a credit. Verified against a real invoice (2026-09-25): starting -500, ending 0, subtotal
        // 79000, amount_paid 78500 -> 5.00 consumed. The previous formula had this inverted
        // (starting < ending returned 0), so it recorded 0.00 for exactly the case it exists for.
        long starting = invoice.StartingBalance;
        long ending = invoice.EndingBalance ?? starting;
        decimal balanceCredit = starting < 0 && ending > starting
            ? (Math.Min(ending, 0L) - starting) / 100m
            : 0m;

        // PeriodStart comes off the first invoice line item (Stripe.net 52.x has no top-level
        // property). The PaymentIntent is only present when Invoice.payments was expanded — Stripe
        // omits it from webhook payloads, so a null here is normal and resolved separately.
        DateTime? periodStart = invoice.Lines?.Data?.FirstOrDefault()?.Period?.Start;
        string? paymentIntentId = invoice.Payments?.Data?.FirstOrDefault()?.Payment?.PaymentIntentId;

        // invoice.PeriodEnd is the PREVIOUS period's end on a renewal — see InvoicePeriodRules.
        DateTime periodEnd = InvoicePeriodRules.CurrentPeriodEnd(
            invoice.Lines?.Data?.Select(l => l.Period?.End) ?? [], invoice.PeriodEnd);

        return new StripeInvoiceInfo(
            invoice.Id, invoice.AmountPaid / 100m, discountAmount + balanceCredit, invoice.Currency,
            invoice.StatusTransitions?.PaidAt ?? DateTime.UtcNow, periodStart, periodEnd, paymentIntentId);
    }
}
