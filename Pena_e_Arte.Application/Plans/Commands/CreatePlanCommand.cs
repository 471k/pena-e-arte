using FluentValidation;
using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Plans.Commands;

public record CreatePlanCommand(CreatePlanRequest Request) : IRequest<PlanResponse>;

public class CreatePlanHandler(IAppDbContext db, IStripeBillingService stripe)
    : IRequestHandler<CreatePlanCommand, PlanResponse>
{
    public async Task<PlanResponse> Handle(CreatePlanCommand command, CancellationToken ct)
    {
        CreatePlanRequest req = command.Request;

        Plan plan = new()
        {
            Name = req.Name,
            YearlyDiscountPercent = req.YearlyDiscountPercent,
            AllowBrandingRemoval = req.AllowBrandingRemoval,
            MaxArtists = req.MaxArtists,
            MaxAppointmentsPerMonth = req.MaxAppointmentsPerMonth,
            MaxNotificationsPerMonth = req.MaxNotificationsPerMonth,
            MaxStorageGb = req.MaxStorageGb,
            MaxLocations = req.MaxLocations,
            AllowApiAccess = req.AllowApiAccess,
            PrioritySupport = req.PrioritySupport,
            AllowMarketingCampaigns = req.AllowMarketingCampaigns,
        };

        foreach (PlanPriceRequest pr in req.Prices)
        {
            BillingInterval interval = Enum.Parse<BillingInterval>(pr.Interval, ignoreCase: true);

            // G3 — a new plan's linked Stripe price must match its amount/interval too, or
            // MRR would be wrong from the very first subscriber.
            if (pr.StripePriceId is not null)
                await ValidateStripePriceAsync(stripe, pr.StripePriceId, pr.Price, interval, ct);

            plan.Prices.Add(new PlanPrice
            {
                Interval = interval,
                Price = pr.Price,
                StripePriceId = pr.StripePriceId,
                IsActive = pr.IsActive,
            });
        }

        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);

        return Map(plan, subscriberCount: 0);
    }

    // G3 — shared with UpdatePlanHandler, which calls this directly (see architecture.md
    // Decisions Log, "One MRR definition (2026-09-23)").
    internal static async Task ValidateStripePriceAsync(
        IStripeBillingService stripe, string stripePriceId, decimal price, BillingInterval interval, CancellationToken ct)
    {
        StripePriceInfo? info = await stripe.GetPriceAsync(stripePriceId, ct);
        if (info is null)
            throw new BusinessRuleViolationException($"Stripe price {stripePriceId} was not found.");
        if (!info.Active)
            throw new BusinessRuleViolationException($"Stripe price {stripePriceId} is not active.");

        string expectedInterval = interval == BillingInterval.Monthly ? "month" : "year";
        decimal stripeAmount = (info.UnitAmount ?? 0) / 100m;

        if (stripeAmount != price || info.RecurringInterval != expectedInterval || info.IntervalCount != 1)
        {
            string stripeIntervalLabel = info.RecurringInterval ?? "unknown interval";
            throw new BusinessRuleViolationException(
                $"Stripe price {stripePriceId} is €{stripeAmount:0.00}/{stripeIntervalLabel}; "
                + $"this plan price is €{price:0.00}.");
        }
    }

    // YearlySavingAmount/YearlyMonthsFree are left null here — this Map is used only by
    // the admin create/update response, which the editor doesn't read (it works off
    // YearlyDiscountPercent, see PlanEditPage). GetPlansHandler computes the real values
    // for the owner-facing catalogue (D7).
    internal static PlanResponse Map(Plan plan, int subscriberCount) => new(
        plan.Id, plan.Name, plan.YearlyDiscountPercent, plan.AllowBrandingRemoval,
        subscriberCount,
        plan.MaxArtists, plan.MaxAppointmentsPerMonth, plan.MaxNotificationsPerMonth,
        plan.MaxStorageGb, plan.MaxLocations, plan.AllowApiAccess, plan.PrioritySupport,
        plan.AllowMarketingCampaigns,
        plan.Prices.Select(pp => new PlanPriceResponse(
            pp.Id, pp.Interval.ToString(), pp.Price, pp.StripePriceId, pp.IsActive)).ToList(),
        null, null);
}

public class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.YearlyDiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.Prices).NotEmpty()
            .WithMessage("At least one billing interval must be provided.");
        RuleForEach(x => x.Request.Prices).ChildRules(price =>
        {
            price.RuleFor(p => p.Interval)
                .NotEmpty()
                .Must(v => Enum.TryParse<BillingInterval>(v, ignoreCase: true, out _))
                .WithMessage("Interval must be 'Monthly' or 'Yearly'.");
            price.RuleFor(p => p.Price).GreaterThanOrEqualTo(0);
        });
        RuleFor(x => x.Request.Prices)
            .Must(prices => prices
                .Select(p => p.Interval.ToUpperInvariant())
                .Distinct().Count() == prices.Count)
            .WithMessage("Each billing interval may only appear once.");
        // A plan is either fully free (lead-gen tier) or fully paid — never a mix of a
        // free interval alongside a paid one.
        RuleFor(x => x.Request.Prices)
            .Must(prices => prices.Count == 0
                || prices.All(p => p.Price == 0) || prices.All(p => p.Price > 0))
            .WithMessage("A plan must be either fully free (all prices = 0) or fully paid (all prices > 0).");
        RuleFor(x => x.Request.MaxArtists).GreaterThan(0)
            .When(x => x.Request.MaxArtists is not null);
        RuleFor(x => x.Request.MaxAppointmentsPerMonth).GreaterThan(0)
            .When(x => x.Request.MaxAppointmentsPerMonth is not null);
        RuleFor(x => x.Request.MaxNotificationsPerMonth).GreaterThan(0)
            .When(x => x.Request.MaxNotificationsPerMonth is not null);
        RuleFor(x => x.Request.MaxStorageGb).GreaterThan(0)
            .When(x => x.Request.MaxStorageGb is not null);
        RuleFor(x => x.Request.MaxLocations).GreaterThan(0)
            .When(x => x.Request.MaxLocations is not null);
    }
}
