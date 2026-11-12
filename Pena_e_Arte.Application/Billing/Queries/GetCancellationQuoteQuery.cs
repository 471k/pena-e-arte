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

        // Built by CancellationQuotes, the same builder the cancel command uses to check that the
        // quote it is about to honour is still this one (E05b-2). Monthly (card-billed) and
        // cash-billed subscriptions report a zero-refund quote: A3, no refund formula.
        SubscriptionInvoicePayment? invoice = await CancellationQuotes.LatestInvoiceAsync(db, subscription, ct);
        return CancellationQuotes.Build(subscription, invoice, DateTime.UtcNow).Response;
    }
}
