namespace Pena_e_Arte.Application.Billing;

/// <summary>
/// Which date an <c>invoice.paid</c> should leave in Subscription.CurrentPeriodEnd. Stripe's
/// <c>invoice.period_end</c> is the end of the PREVIOUS (usage) period for a renewal invoice:
/// verified against a real test-mode renewal, where the invoice said 2026-09-24 -> 2026-10-24 but
/// its line item and the subscription both said 2026-10-24 -> 2026-11-24. Trusting it stamped the
/// old period end over the correct value that customer.subscription.updated had just delivered.
/// The line items carry the period actually being paid for.
/// </summary>
public static class InvoicePeriodRules
{
    public static DateTime CurrentPeriodEnd(IEnumerable<DateTime?> lineItemPeriodEnds, DateTime invoicePeriodEnd)
    {
        DateTime[] ends = lineItemPeriodEnds.OfType<DateTime>().ToArray();
        return ends.Length == 0 ? invoicePeriodEnd : ends.Max();
    }
}
