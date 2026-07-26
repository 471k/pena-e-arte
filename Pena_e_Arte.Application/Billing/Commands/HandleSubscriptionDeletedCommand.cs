using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleSubscriptionDeletedCommand(string StripeSubscriptionId) : IRequest;

public class HandleSubscriptionDeletedHandler(IAppDbContext db) : IRequestHandler<HandleSubscriptionDeletedCommand>
{
    public async Task Handle(HandleSubscriptionDeletedCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelAtPeriodEnd = false;
        await db.SaveChangesAsync(ct);
    }
}
