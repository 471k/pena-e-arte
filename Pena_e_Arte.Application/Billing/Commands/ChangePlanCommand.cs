using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

public record ChangePlanCommand(ChangePlanRequest Request) : IRequest<SubscriptionResponse>;

/// <summary>
/// Switches an active Stripe-billed subscription to another plan.
/// Upgrades (higher monthly-equivalent price) apply immediately with proration;
/// downgrades are scheduled for the end of the current billing period.
/// </summary>
public class ChangePlanHandler(
    IAppDbContext              db,
    ICurrentTenant             tenant,
    IStripeBillingService      billing,
    ILogger<ChangePlanHandler> logger)
    : IRequestHandler<ChangePlanCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(ChangePlanCommand command, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        if (subscription.Status != SubscriptionStatus.Active)
            throw new BusinessRuleViolationException(
                "Plan changes require an active subscription. Use the subscribe flow instead.");

        if (subscription.StripeSubscriptionId is null)
            throw new BusinessRuleViolationException(
                "This subscription is billed outside Stripe. Contact the platform to change plans.");

        if (subscription.PendingPlanId is not null)
            throw new BusinessRuleViolationException(
                "A plan change is already scheduled. Cancel it before choosing another plan.");

        if (subscription.PlanId == command.Request.PlanId)
            throw new BusinessRuleViolationException("The studio is already on this plan.");

        Domain.Entities.Plan newPlan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.Request.PlanId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Plan), command.Request.PlanId);

        Domain.Entities.Plan currentPlan = subscription.Plan
            ?? throw new BusinessRuleViolationException(
                "The current plan could not be determined. Contact the platform.");

        string? newPriceId     = ChargedPriceId(newPlan);
        string? currentPriceId = ChargedPriceId(currentPlan);

        if (newPriceId is null || currentPriceId is null)
            throw new BusinessRuleViolationException(
                "The selected plan is not available for online billing. Contact the platform.");

        if (MonthlyEquivalent(newPlan) > MonthlyEquivalent(currentPlan))
        {
            // Upgrade — switch now, charge the prorated difference immediately
            DateTime periodEnd = await billing.ChangeSubscriptionPriceAsync(
                subscription.StripeSubscriptionId, newPriceId, ct);

            subscription.PlanId           = newPlan.Id;
            subscription.CurrentPeriodEnd = periodEnd;

            logger.LogInformation(
                "Plan upgraded immediately for studio {@StudioId} to plan {@PlanId}",
                subscription.StudioId, newPlan.Id);
        }
        else
        {
            // Downgrade — the studio keeps what it paid for; switch at period end
            string newPriceInterval = newPlan.BillingInterval == BillingInterval.Monthly ? "month" : "year";
            await billing.ScheduleSubscriptionPriceChangeAsync(
                subscription.StripeSubscriptionId, currentPriceId, newPriceId, newPriceInterval, ct);

            subscription.PendingPlanId = newPlan.Id;

            logger.LogInformation(
                "Plan downgrade scheduled at period end for studio {@StudioId} to plan {@PlanId}",
                subscription.StudioId, newPlan.Id);
        }

        await db.SaveChangesAsync(ct);
        return CreateSubscriptionHandler.Map(subscription);
    }

    private static string? ChargedPriceId(Domain.Entities.Plan plan) =>
        plan.BillingInterval == BillingInterval.Monthly
            ? plan.StripePriceIdMonthly
            : plan.StripePriceIdYearly;

    // Normalise to a per-month cost so monthly and yearly plans compare fairly
    private static decimal MonthlyEquivalent(Domain.Entities.Plan plan) =>
        plan.BillingInterval == BillingInterval.Monthly
            ? plan.PriceMonthly
            : plan.PriceYearly / 12m;
}
