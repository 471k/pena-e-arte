using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing;

/// <summary>A cancellation quote and what produced it. <see cref="Yearly"/> and <see cref="Invoice"/> are set
/// only when a refund formula applies and an invoice could be quoted against.</summary>
public sealed record CancellationQuote(
    CancellationQuoteResponse Response, YearlyRefundQuote? Yearly, SubscriptionInvoicePayment? Invoice);

/// <summary>
/// One place that builds the owner's cancellation quote, shared by GET /billing/subscription/cancel/quote and
/// the cancel command (E05b-2, O07). The command rebuilds the quote at execution and compares it with the one
/// the owner confirmed (<see cref="Matches"/>), so "the number shown is the number refunded" is enforced, not
/// assumed. The refund formula itself stays in <see cref="YearlyRefundCalculator"/>, untouched.
/// </summary>
public static class CancellationQuotes
{
    /// <summary>A3: only a Yearly, Stripe-billed subscription has a refund formula. Monthly card-billed and
    /// cash-billed (always Monthly in this system) are never refunded, by design.</summary>
    public static bool HasRefundFormula(Subscription subscription) =>
        subscription.BillingInterval == BillingInterval.Yearly && subscription.StripeSubscriptionId is not null;

    /// <summary>The invoice a yearly refund is computed from: the latest paid one. Null when no formula applies.</summary>
    public static Task<SubscriptionInvoicePayment?> LatestInvoiceAsync(
        IAppDbContext db, Subscription subscription, CancellationToken ct) =>
        HasRefundFormula(subscription)
            ? db.SubscriptionInvoicePayments
                .Where(p => p.SubscriptionId == subscription.Id)
                .OrderByDescending(p => p.PaidAt)
                .FirstOrDefaultAsync(ct)
            : Task.FromResult<SubscriptionInvoicePayment?>(null);

    public static CancellationQuote Build(Subscription subscription, SubscriptionInvoicePayment? invoice, DateTime now)
    {
        if (!HasRefundFormula(subscription))
        {
            // Card-billed monthly keeps access to the end of the paid period (cancel at period end). Cash-billed
            // loses access today: CancelMySubscriptionHandler cancels it immediately, so the quote says so
            // instead of promising a period end the cancellation won't honour.
            DateTime accessEnd = subscription.StripeSubscriptionId is null ? now : subscription.CurrentPeriodEnd;
            return new CancellationQuote(
                new CancellationQuoteResponse("Monthly", 0m, null, accessEnd, null, null, null), null, null);
        }

        YearlyRefundQuote? yearly = YearlyRefundCalculator.QuoteFor(invoice, now);
        return yearly is null || invoice is null
            ? new CancellationQuote(new CancellationQuoteResponse("Yearly", 0m, null, now, null, null, null), null, null)
            : new CancellationQuote(
                new CancellationQuoteResponse(
                    "Yearly", yearly.RefundAmount, yearly.MonthsUsed, now, yearly.AmountPaid,
                    yearly.MonthlyReferencePrice, invoice.Id),
                yearly,
                invoice);
    }

    /// <summary>
    /// Whether the quote the owner confirmed is still the quote: same interval, same refund, and the same
    /// inputs (amount paid, months used, invoice). The access end date isn't compared: for an immediate
    /// cancellation it is "now", which moves by itself without changing anything the owner agreed to.
    /// </summary>
    public static bool Matches(CancellationQuoteResponse current, CancelMySubscriptionRequest confirmed) =>
        current.BillingInterval == confirmed.BillingInterval
        && current.RefundAmount == confirmed.RefundAmount
        && current.MonthsUsed == confirmed.MonthsUsed
        && current.AmountPaid == confirmed.AmountPaid
        && current.InvoiceId == confirmed.InvoiceId;
}
