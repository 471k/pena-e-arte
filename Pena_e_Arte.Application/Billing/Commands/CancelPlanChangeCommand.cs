using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

public record CancelPlanChangeCommand : IRequest<SubscriptionResponse>;

/// <summary>Cancels a scheduled downgrade — the studio stays on its current plan.</summary>
public class CancelPlanChangeHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IStripeBillingService billing,
    ILogger<CancelPlanChangeHandler> logger)
    : IRequestHandler<CancelPlanChangeCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(CancelPlanChangeCommand command, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        if (subscription.PendingPlanId is null)
            throw new BusinessRuleViolationException("There is no pending plan change to cancel.");

        if (subscription.StripeSubscriptionId is not null)
            await billing.CancelScheduledPriceChangeAsync(subscription.StripeSubscriptionId, ct);

        subscription.PendingPlanId = null;
        subscription.PendingBillingInterval = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Scheduled plan change cancelled for studio {@StudioId}", subscription.StudioId);

        return CreateSubscriptionHandler.Map(subscription);
    }
}
