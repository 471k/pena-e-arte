using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Commands;

public record HandleSubscriptionUpdatedCommand(
    string   StripeSubscriptionId,
    string   StripeStatus,
    DateTime CurrentPeriodEnd,
    string?  StripePriceId) : IRequest;

public class HandleSubscriptionUpdatedHandler(IAppDbContext db) : IRequestHandler<HandleSubscriptionUpdatedCommand>
{
    public async Task Handle(HandleSubscriptionUpdatedCommand command, CancellationToken ct)
    {
        Domain.Entities.Subscription? subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null) return;

        subscription.Status = command.StripeStatus switch
        {
            "active"   => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "trialing" => SubscriptionStatus.Trialing,
            "canceled" => SubscriptionStatus.Cancelled,
            _          => subscription.Status
        };

        subscription.CurrentPeriodEnd = command.CurrentPeriodEnd;

        if (command.StripePriceId is not null)
        {
            Domain.Entities.Plan? plan = await db.Plans.FirstOrDefaultAsync(
                p => p.StripePriceIdMonthly == command.StripePriceId ||
                     p.StripePriceIdYearly  == command.StripePriceId, ct);

            if (plan is not null)
                subscription.PlanId = plan.Id;
        }

        await db.SaveChangesAsync(ct);
    }
}
