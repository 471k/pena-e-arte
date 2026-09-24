using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Revenue;

/// <summary>One refund definition, shared by the owner-facing cancellation quote and both the
/// owner and admin cancel paths — same reasoning as MrrRules' "one MRR definition": the quote
/// an owner sees before confirming must be exactly the amount actually refunded, computed
/// once. See architecture.md Decisions Log, "Yearly cancellation refunds (2026-09-24)".</summary>
public sealed record YearlyRefundQuote(
    int MonthsUsed, decimal AmountPaid, decimal MonthlyReferencePrice, decimal RefundAmount);

public static class YearlyRefundCalculator
{
    /// <summary>A started month counts in full — count by monthly anniversaries of the period
    /// start (A1). .NET AddMonths already clamps month-end dates the way A1 requires (31 Jan
    /// to 28/29 Feb) — no extra clamping logic needed.</summary>
    public static int MonthsUsed(DateTime periodStart, DateTime cancelledAt)
    {
        int months = 0;
        while (periodStart.AddMonths(months + 1) <= cancelledAt) months++;
        return months + 1; // cancelling on day 1 of the period = 1 month used
    }

    public static decimal Compute(decimal amountPaid, int monthsUsed, decimal monthlyReferencePrice) =>
        Math.Max(0m, amountPaid - monthsUsed * monthlyReferencePrice);

    /// <summary>Null when there is no billed invoice to refund against (no payment yet, or a
    /// pre-Batch-2b/pre-Batch-3a invoice with no PeriodStart snapshot).</summary>
    public static YearlyRefundQuote? QuoteFor(SubscriptionInvoicePayment? latestInvoice, DateTime now)
    {
        if (latestInvoice?.PeriodStart is not DateTime periodStart) return null;
        int months = MonthsUsed(periodStart, now);
        decimal monthlyRef = latestInvoice.MonthlyReferencePrice ?? 0m;
        decimal refund = Compute(latestInvoice.AmountPaid, months, monthlyRef);
        return new YearlyRefundQuote(months, latestInvoice.AmountPaid, monthlyRef, refund);
    }
}
