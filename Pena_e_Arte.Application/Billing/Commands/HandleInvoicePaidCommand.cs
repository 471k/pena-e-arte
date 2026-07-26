using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleInvoicePaidCommand(string StripeSubscriptionId, DateTime PeriodEnd) : IRequest;

public class HandleInvoicePaidHandler(IAppDbContext db) : IRequestHandler<HandleInvoicePaidCommand>
{
    public async Task Handle(HandleInvoicePaidCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = command.PeriodEnd;

        await db.SaveChangesAsync(ct);
    }
}
