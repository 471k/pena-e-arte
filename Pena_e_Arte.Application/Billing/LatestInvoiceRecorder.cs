using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing;

/// <summary>
/// Records a subscription's latest paid invoice right after the subscription is linked locally.
/// The invoice.paid webhook for a NEW subscription is emitted at the same moment as
/// checkout.session.completed and can reach the API before the handler has stored
/// StripeSubscriptionId; HandleInvoicePaidHandler then finds no subscription and returns, and Stripe
/// never retries a 200 — so the first invoice (for a Yearly plan, exactly the one a cancellation
/// refund is quoted from) was silently never recorded. Found in a real Stripe test-mode run.
/// Idempotent by StripeInvoiceId (HandleInvoicePaidHandler skips an already-recorded invoice), so it
/// is safe alongside the webhook whichever wins. Best-effort: a failure logs and never fails the
/// activation the customer just paid for.
/// </summary>
public static class LatestInvoiceRecorder
{
    public static async Task RecordAsync(
        IStripeBillingService billing, ISender sender, ILogger logger,
        string stripeSubscriptionId, CancellationToken ct)
    {
        try
        {
            StripeInvoiceInfo? invoice = await billing.GetLatestPaidInvoiceAsync(stripeSubscriptionId, ct);
            if (invoice is null) return;

            await sender.Send(new HandleInvoicePaidCommand(
                stripeSubscriptionId, invoice.PeriodEnd, invoice.InvoiceId, invoice.AmountPaid,
                invoice.DiscountAmount, invoice.Currency, invoice.PaidAt, invoice.PeriodStart,
                invoice.PaymentIntentId), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not record the first paid invoice for subscription {StripeSubscriptionId}; "
                + "the invoice.paid webhook may still record it",
                stripeSubscriptionId);
        }
    }
}
