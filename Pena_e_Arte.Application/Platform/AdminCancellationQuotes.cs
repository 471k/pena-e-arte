using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform;

/// <summary>What one admin cancellation refunds under one rule. Invoice is null when no refund applies.</summary>
public sealed record AdminCancellationOutcome(
    bool RefundApplies, decimal RefundAmount, int? MonthsUsed, RefundRule Rule, SubscriptionInvoicePayment? Invoice);

/// <summary>
/// The admin cancellation's consequence, built in one place for both the quote the admin reviews and the command that
/// carries it out (slice 8 phase 3, D03), so "the refund shown is the refund issued" is enforced rather than assumed.
/// The owner-side equivalent is <see cref="CancellationQuotes"/>; the two differ because an admin cancel is immediate
/// and can override the formula. The formula itself stays in <see cref="YearlyRefundCalculator"/>.
/// </summary>
public static class AdminCancellationQuotes
{
    public static readonly IReadOnlySet<SubscriptionStatus> Cancellable = new HashSet<SubscriptionStatus>
    {
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue,
        SubscriptionStatus.Trialing,
        SubscriptionStatus.GracePeriod,
    };

    /// <summary>The override as sent: null (the formula), or the NAME AdminFull / AdminNone in any case. Names only:
    /// Enum.TryParse would also accept "1" or "2" and silently map a number to a refund rule.</summary>
    public static bool TryParseOverride(string? text, out RefundRule? rule)
    {
        rule = null;
        if (text is null) return true;
        if (string.Equals(text, nameof(RefundRule.AdminFull), StringComparison.OrdinalIgnoreCase))
        {
            rule = RefundRule.AdminFull;
            return true;
        }
        if (string.Equals(text, nameof(RefundRule.AdminNone), StringComparison.OrdinalIgnoreCase))
        {
            rule = RefundRule.AdminNone;
            return true;
        }
        return false;
    }

    /// <summary><paramref name="invoice"/> comes from <see cref="CancellationQuotes.LatestInvoiceAsync"/>, which is null
    /// unless the subscription is Yearly and Stripe-billed. No invoice: nothing is refunded and no refund row is written,
    /// whatever the override.</summary>
    public static AdminCancellationOutcome Outcome(SubscriptionInvoicePayment? invoice, RefundRule? overrideRule, DateTime now)
    {
        if (invoice is null)
            return new AdminCancellationOutcome(false, 0m, null, overrideRule ?? RefundRule.YearlyFormula, null);

        return overrideRule switch
        {
            RefundRule.AdminFull => new AdminCancellationOutcome(true, invoice.AmountPaid, null, RefundRule.AdminFull, invoice),
            RefundRule.AdminNone => new AdminCancellationOutcome(true, 0m, null, RefundRule.AdminNone, invoice),
            _ => YearlyRefundCalculator.QuoteFor(invoice, now) is YearlyRefundQuote q
                ? new AdminCancellationOutcome(true, q.RefundAmount, q.MonthsUsed, RefundRule.YearlyFormula, invoice)
                : new AdminCancellationOutcome(true, 0m, null, RefundRule.YearlyFormula, invoice),
        };
    }

    public static AdminCancellationQuoteResponse Build(Subscription subscription, SubscriptionInvoicePayment? invoice, DateTime now)
    {
        AdminCancellationOutcome formula = Outcome(invoice, null, now);
        return new AdminCancellationQuoteResponse(
            subscription.Status.ToString(),
            subscription.Plan?.Name,
            subscription.BillingInterval.ToString(),
            subscription.StripeSubscriptionId is not null,
            formula.RefundApplies,
            invoice?.Currency ?? subscription.BilledCurrency,
            formula.RefundAmount,
            invoice?.AmountPaid ?? 0m,
            formula.MonthsUsed,
            invoice?.AmountPaid,
            invoice?.Id);
    }
}
