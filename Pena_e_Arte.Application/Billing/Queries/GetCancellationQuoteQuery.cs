using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Queries;

/// <summary>The quote an owner sees before confirming a cancellation — computed with the exact
/// same YearlyRefundCalculator that the cancel commands use, so the number shown is always the
/// number actually refunded. Monthly and cash-billed subscriptions report a zero-refund quote
/// (their cancellation policy is "no refund" by design, not an edge case of the formula).</summary>
public record GetCancellationQuoteQuery : IRequest<CancellationQuoteResponse>;

public class GetCancellationQuoteHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetCancellationQuoteQuery, CancellationQuoteResponse>
{
    public async Task<CancellationQuoteResponse> Handle(GetCancellationQuoteQuery query, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        // Monthly (card-billed) and cash-billed subscriptions are never refunded — A3: only a
        // Yearly, Stripe-billed subscription has a refund formula. Cash-billed is always
        // Monthly in this system (§3 flag — no Yearly cash-billed case exists), so reporting
        // "Monthly" here is correct for both arms of this condition.
        if (subscription.BillingInterval != BillingInterval.Yearly || subscription.StripeSubscriptionId is null)
            return new CancellationQuoteResponse("Monthly", 0m, null, subscription.CurrentPeriodEnd, null, null);

        SubscriptionInvoicePayment? invoice = await db.SubscriptionInvoicePayments
            .Where(p => p.SubscriptionId == subscription.Id)
            .OrderByDescending(p => p.PaidAt)
            .FirstOrDefaultAsync(ct);

        YearlyRefundQuote? quote = YearlyRefundCalculator.QuoteFor(invoice, DateTime.UtcNow);
        return quote is null
            ? new CancellationQuoteResponse("Yearly", 0m, null, DateTime.UtcNow, null, null)
            : new CancellationQuoteResponse(
                "Yearly", quote.RefundAmount, quote.MonthsUsed, DateTime.UtcNow, quote.AmountPaid, quote.MonthlyReferencePrice);
    }
}
