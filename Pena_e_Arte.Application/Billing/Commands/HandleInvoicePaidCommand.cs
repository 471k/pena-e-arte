using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

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
    string? StripePaymentIntentId = null) : IRequest;

public class HandleInvoicePaidHandler(IAppDbContext db) : IRequestHandler<HandleInvoicePaidCommand>
{
    public async Task Handle(HandleInvoicePaidCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .Include(s => s.Plan)
                .ThenInclude(p => p!.Prices)
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = command.PeriodEnd;

        bool alreadyRecorded = await db.SubscriptionInvoicePayments
            .AnyAsync(p => p.StripeInvoiceId == command.StripeInvoiceId, ct);
        if (!alreadyRecorded)
        {
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
                StripePaymentIntentId = command.StripePaymentIntentId,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
