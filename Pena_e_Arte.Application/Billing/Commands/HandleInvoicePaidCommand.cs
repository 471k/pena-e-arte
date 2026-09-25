using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleInvoicePaidCommand(
    string StripeSubscriptionId, DateTime PeriodEnd,
    string StripeInvoiceId, decimal AmountPaid, decimal DiscountAmount,
    string Currency, DateTime PaidAt,
    // Yearly-cancellation-refund snapshot (Batch 3a) — see architecture.md Decisions Log,
    // "Yearly cancellation refunds (2026-09-24)". All three are best-effort: null when the
    // Stripe payload didn't resolve them, which YearlyRefundCalculator.QuoteFor treats as
    // "no quote available" rather than guessing.
    DateTime? PeriodStart = null,
    string? StripePaymentIntentId = null,
    string? StripeEventId = null) : IRequest;

public class HandleInvoicePaidHandler(
    IAppDbContext db,
    IStripeBillingService stripe,
    ILogger<HandleInvoicePaidHandler> logger)
    : IRequestHandler<HandleInvoicePaidCommand>
{
    public async Task Handle(HandleInvoicePaidCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .Include(s => s.Plan)
                .ThenInclude(p => p!.Prices)
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        SubscriptionStatus previousStatus = subscription.Status;
        decimal mrr = MrrRules.MonthlyEquivalent(subscription);

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = command.PeriodEnd;

        // Stripe delivers invoice.paid and customer.subscription.updated for a recovered payment in
        // either order (observed: invoice.paid first, in the same second). Whichever handler flips
        // PastDue -> Active first must record the Recovered, or the ledger keeps a paying
        // subscription excluded from MRR indefinitely. HandleSubscriptionUpdatedHandler does the same
        // check, and only one of the two can observe PastDue.
        if (previousStatus == SubscriptionStatus.PastDue)
        {
            subscription.PastDueSince = null;
            RevenueEventRecorder.Record(db, subscription, mrr, mrr, RevenueEventType.Recovered,
                nameof(HandleInvoicePaidHandler), command.StripeEventId);
        }

        bool alreadyRecorded = await db.SubscriptionInvoicePayments
            .AnyAsync(p => p.StripeInvoiceId == command.StripeInvoiceId, ct);
        if (!alreadyRecorded)
        {
            // The webhook payload never carries the PaymentIntent (Invoice.payments is expandable and
            // omitted), so resolve it here — only for Yearly invoices, the only ones a refund can
            // target. Best-effort: a null just means "no refund quote available", never a failed webhook.
            string? paymentIntentId = command.StripePaymentIntentId;
            if (paymentIntentId is null && subscription.BillingInterval == BillingInterval.Yearly)
            {
                try
                {
                    paymentIntentId = await stripe.GetInvoicePaymentIntentIdAsync(command.StripeInvoiceId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Could not resolve the PaymentIntent for invoice {StripeInvoiceId}; the refund quote for it will be unavailable",
                        command.StripeInvoiceId);
                }
            }

            // MonthlyReferencePrice: the tier's Monthly PlanPrice.Price at the moment this
            // invoice paid — Yearly invoices only. Snapshotted so a later Monthly price
            // change never retroactively changes what an already-issued refund would have
            // been (§2.1.A).
            decimal? monthlyReferencePrice = subscription.BillingInterval == BillingInterval.Yearly
                ? subscription.Plan?.Prices.FirstOrDefault(pp => pp.Interval == BillingInterval.Monthly)?.Price
                : null;

            db.SubscriptionInvoicePayments.Add(new SubscriptionInvoicePayment
            {
                SubscriptionId = subscription.Id,
                StudioId = subscription.StudioId,
                StripeInvoiceId = command.StripeInvoiceId,
                AmountPaid = command.AmountPaid,
                DiscountAmount = command.DiscountAmount,
                Currency = command.Currency,
                PaidAt = command.PaidAt,
                PeriodStart = command.PeriodStart,
                MonthlyReferencePrice = monthlyReferencePrice,
                StripePaymentIntentId = paymentIntentId,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
