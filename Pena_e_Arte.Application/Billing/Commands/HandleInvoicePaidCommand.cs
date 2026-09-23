using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleInvoicePaidCommand(
    string StripeSubscriptionId, DateTime PeriodEnd,
    string StripeInvoiceId, decimal AmountPaid, decimal DiscountAmount,
    string Currency, DateTime PaidAt) : IRequest;

public class HandleInvoicePaidHandler(IAppDbContext db) : IRequestHandler<HandleInvoicePaidCommand>
{
    public async Task Handle(HandleInvoicePaidCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = command.PeriodEnd;

        bool alreadyRecorded = await db.SubscriptionInvoicePayments
            .AnyAsync(p => p.StripeInvoiceId == command.StripeInvoiceId, ct);
        if (!alreadyRecorded)
        {
            db.SubscriptionInvoicePayments.Add(new SubscriptionInvoicePayment
            {
                SubscriptionId = subscription.Id,
                StudioId = subscription.StudioId,
                StripeInvoiceId = command.StripeInvoiceId,
                AmountPaid = command.AmountPaid,
                DiscountAmount = command.DiscountAmount,
                Currency = command.Currency,
                PaidAt = command.PaidAt,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
