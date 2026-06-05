using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

public record CreateSubscriptionCommand(CreateSubscriptionRequest Request) : IRequest<SubscriptionResponse>;

public class CreateSubscriptionHandler(
    IAppDbContext          db,
    ICurrentTenant         tenant,
    IStripeBillingService  billing)
    : IRequestHandler<CreateSubscriptionCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(CreateSubscriptionCommand command, CancellationToken ct)
    {
        Domain.Entities.Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.Request.PlanId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Plan), command.Request.PlanId);

        Domain.Entities.Subscription subscription = await db.Subscriptions
            .Include(s => s.Studio)
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Subscription), tenant.StudioId);

        if (subscription.Status == SubscriptionStatus.Active)
            throw new BusinessRuleViolationException("Studio already has an active subscription.");

        string? priceId = plan.BillingInterval == BillingInterval.Monthly
            ? plan.StripePriceIdMonthly
            : plan.StripePriceIdYearly;

        DateTime periodEnd;

        if (priceId is not null && subscription.Studio is not null)
        {
            if (subscription.Studio.StripeCustomerId is null)
            {
                subscription.Studio.StripeCustomerId =
                    await billing.CreateCustomerAsync(subscription.Studio.OwnerEmail, ct);
            }

            (string stripeSubId, periodEnd) = await billing.CreateSubscriptionAsync(
                subscription.Studio.StripeCustomerId!, priceId, ct);

            subscription.StripeSubscriptionId = stripeSubId;
        }
        else
        {
            periodEnd = DateTime.UtcNow.AddMonths(1);
        }

        subscription.PlanId           = command.Request.PlanId;
        subscription.Status           = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = periodEnd;

        await db.SaveChangesAsync(ct);

        return Map(subscription);
    }

    internal static SubscriptionResponse Map(Domain.Entities.Subscription s) => new(
        s.Id, s.StudioId, s.PlanId, s.Status.ToString(),
        s.TrialExpiresAt, s.CurrentPeriodEnd, s.GracePeriodEnd, s.StripeSubscriptionId,
        s.Studio?.StripeAccountId is not null);
}
