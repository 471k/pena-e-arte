using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Billing.Commands;

// Plan navigation set explicitly for change-tracker consistency. Note: SubscriptionResponse
// (returned by CreateSubscriptionHandler.Map) does not carry PlanName, so this does not affect
// the immediate return value — the "no plan" display case is a real data state (Subscription.PlanId
// pointing at a deleted/missing Plan) handled by GetPlatformSubscriptionsQuery + the list page fallback.
public record ActivateSubscriptionManuallyCommand(
    Guid    StudioId,
    Guid    PlanId,
    string? Note)
    : IRequest<SubscriptionResponse>;

public class ActivateSubscriptionManuallyHandler(
    IAppDbContext                               db,
    ILogger<ActivateSubscriptionManuallyHandler> logger)
    : IRequestHandler<ActivateSubscriptionManuallyCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(
        ActivateSubscriptionManuallyCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Plan), command.PlanId);

        if (studio.Subscription is null)
        {
            studio.Subscription = new Subscription
            {
                StudioId         = studio.Id,
                PlanId           = plan.Id,
                Plan             = plan,
                Status           = SubscriptionStatus.Active,
                CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            };
            db.Subscriptions.Add(studio.Subscription);
        }
        else
        {
            studio.Subscription.PlanId           = plan.Id;
            studio.Subscription.Plan             = plan;
            studio.Subscription.Status           = SubscriptionStatus.Active;
            studio.Subscription.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);
            studio.Subscription.TrialExpiresAt   = null;
        }

        // Note is deliberately not logged — free text could contain PII (Rule #3).
        logger.LogInformation(
            "Subscription manually activated for studio {@StudioId} on plan {@PlanId}",
            studio.Id, plan.Id);

        await db.SaveChangesAsync(ct);
        return CreateSubscriptionHandler.Map(studio.Subscription);
    }
}
