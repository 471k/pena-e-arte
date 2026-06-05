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

public class CreateSubscriptionHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateSubscriptionCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(CreateSubscriptionCommand command, CancellationToken ct)
    {
        bool planExists = await db.Plans.AnyAsync(p => p.Id == command.Request.PlanId, ct);
        if (!planExists)
            throw new NotFoundException(nameof(Domain.Entities.Plan), command.Request.PlanId);

        Domain.Entities.Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Subscription), tenant.StudioId);

        if (subscription.Status == SubscriptionStatus.Active)
            throw new BusinessRuleViolationException("Studio already has an active subscription.");

        subscription.PlanId          = command.Request.PlanId;
        subscription.Status          = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);

        await db.SaveChangesAsync(ct);

        return Map(subscription);
    }

    internal static SubscriptionResponse Map(Domain.Entities.Subscription s) => new(
        s.Id, s.StudioId, s.PlanId, s.Status.ToString(),
        s.TrialExpiresAt, s.CurrentPeriodEnd, s.GracePeriodEnd, s.StripeSubscriptionId,
        s.Studio?.StripeAccountId is not null);
}
