using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing;

/// <summary>Shared Stripe-call + SubscriptionRefund-row + idempotency logic used by both the
/// owner-facing cancel flow (CancelMySubscriptionHandler) and the admin override flow
/// (CancelSubscriptionHandler) — put once here rather than duplicated, and kept as a static
/// class (not a Platform.Commands member) to avoid a circular Application-layer dependency
/// between Platform.Commands and Billing.Commands. See architecture.md Decisions Log, "Yearly
/// cancellation refunds (2026-09-24)".</summary>
public static class YearlyRefundIssuer
{
    public static async Task IssueAsync(
        IAppDbContext db, IStripeBillingService billing, Subscription subscription,
        SubscriptionInvoicePayment invoice, YearlyRefundQuote quote, RefundRule rule,
        Guid initiatedByUserId, string? adminReason, ILogger logger, CancellationToken ct)
    {
        // A3 — "cancel sent twice / network retry: exactly one Stripe refund". The DB-level
        // half is the unique index on SubscriptionInvoicePaymentId.
        bool alreadyRefunded = await db.SubscriptionRefunds
            .AnyAsync(r => r.SubscriptionInvoicePaymentId == invoice.Id, ct);
        if (alreadyRefunded) return;

        SubscriptionRefund refund = new()
        {
            SubscriptionId = subscription.Id,
            StudioId = subscription.StudioId,
            SubscriptionInvoicePaymentId = invoice.Id,
            StripeInvoiceId = invoice.StripeInvoiceId,
            Amount = quote.RefundAmount,
            Currency = invoice.Currency,
            MonthsUsed = quote.MonthsUsed,
            AmountPaid = quote.AmountPaid,
            MonthlyReferencePrice = quote.MonthlyReferencePrice,
            Rule = rule,
            InitiatedByUserId = initiatedByUserId,
            AdminReason = adminReason,
        };

        if (quote.RefundAmount > 0 && invoice.StripePaymentIntentId is not null)
        {
            // A3 — idempotency key = subscription id + current period start.
            string idempotencyKey = $"{subscription.StripeSubscriptionId}:{invoice.PeriodStart:O}";
            try
            {
                (string refundId, string status) = await billing.RefundAsync(
                    invoice.StripePaymentIntentId, (long)Math.Round(quote.RefundAmount * 100m), idempotencyKey, ct);
                refund.StripeRefundId = refundId;
                refund.Status = status == "succeeded" ? RefundStatus.Succeeded : RefundStatus.Pending;
            }
            catch (Exception ex)
            {
                // A6: "A Failed refund must alert the admin (error log + visible on the
                // studio's admin page); the studio stays cancelled." The cancellation itself
                // must not be rolled back by a Stripe-side failure — same best-effort pattern
                // CancelSubscriptionHandler already uses for CancelSubscriptionAsync.
                refund.Status = RefundStatus.Failed;
                refund.FailureReason = ex.Message;
                logger.LogError(ex,
                    "Stripe refund failed for subscription {@SubscriptionId} invoice {@StripeInvoiceId}",
                    subscription.Id, invoice.StripeInvoiceId);
            }
        }
        else
        {
            // Zero-amount (AdminNone, or the formula computed 0) — nothing to call Stripe for,
            // but A6 says persist "every refund", including the admin's explicit "no refund".
            refund.Status = RefundStatus.Succeeded;
        }

        db.SubscriptionRefunds.Add(refund);
    }
}
