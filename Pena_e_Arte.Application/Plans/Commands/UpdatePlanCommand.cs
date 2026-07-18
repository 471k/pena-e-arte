using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Plans.Commands;

public record UpdatePlanCommand(Guid PlanId, UpdatePlanRequest Request) : IRequest<PlanResponse>;

public class UpdatePlanHandler(IAppDbContext db)
    : IRequestHandler<UpdatePlanCommand, PlanResponse>
{
    public async Task<PlanResponse> Handle(UpdatePlanCommand command, CancellationToken ct)
    {
        Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Plan), command.PlanId);

        UpdatePlanRequest req = command.Request;

        if (req.PairedPlanId is Guid pairedId)
        {
            if (pairedId == command.PlanId)
                throw new BusinessRuleViolationException("A plan cannot be paired with itself.");

            bool pairedExists = await db.Plans.AnyAsync(p => p.Id == pairedId, ct);
            if (!pairedExists)
                throw new NotFoundException(nameof(Plan), pairedId);
        }

        plan.Name                  = req.Name;
        plan.PriceMonthly          = req.PriceMonthly;
        plan.PriceYearly           = req.PriceYearly;
        plan.YearlyDiscountPercent = req.YearlyDiscountPercent;
        plan.AllowBrandingRemoval  = req.AllowBrandingRemoval;
        plan.StripePriceIdMonthly  = req.StripePriceIdMonthly;
        plan.StripePriceIdYearly   = req.StripePriceIdYearly;

        // Limit/feature fields + the pairing itself — never price, interval, or Stripe IDs.
        plan.MaxArtists               = req.MaxArtists;
        plan.MaxAppointmentsPerMonth  = req.MaxAppointmentsPerMonth;
        plan.MaxNotificationsPerMonth = req.MaxNotificationsPerMonth;
        plan.MaxStorageGb             = req.MaxStorageGb;
        plan.MaxLocations             = req.MaxLocations;
        plan.AllowApiAccess           = req.AllowApiAccess;
        plan.PrioritySupport          = req.PrioritySupport;
        plan.PairedPlanId             = req.PairedPlanId;

        // Keep the paired row's limits/feature flags in sync — a tier's Monthly and
        // Yearly rows represent the same product, just billed differently. Price,
        // BillingInterval, and Stripe price IDs are intentionally excluded: those stay
        // per-row (see Decisions Log — "Plan billing interval stays locked per-row").
        if (plan.PairedPlanId is Guid linkedId)
        {
            Plan? paired = await db.Plans.FirstOrDefaultAsync(p => p.Id == linkedId, ct);
            if (paired is not null)
            {
                paired.MaxArtists               = plan.MaxArtists;
                paired.MaxAppointmentsPerMonth  = plan.MaxAppointmentsPerMonth;
                paired.MaxNotificationsPerMonth = plan.MaxNotificationsPerMonth;
                paired.MaxStorageGb             = plan.MaxStorageGb;
                paired.MaxLocations             = plan.MaxLocations;
                paired.AllowApiAccess           = plan.AllowApiAccess;
                paired.PrioritySupport          = plan.PrioritySupport;
                paired.AllowBrandingRemoval     = plan.AllowBrandingRemoval;

                // Keep the link symmetric even if it was only set on one side before.
                paired.PairedPlanId ??= plan.Id;
            }
        }

        await db.SaveChangesAsync(ct);

        int subscriberCount = await db.Subscriptions
            .CountAsync(s => s.PlanId == plan.Id, ct);

        return CreatePlanHandler.Map(plan, subscriberCount);
    }
}

public class UpdatePlanValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.PriceMonthly).GreaterThan(0);
        RuleFor(x => x.Request.PriceYearly).GreaterThan(0);
        RuleFor(x => x.Request.YearlyDiscountPercent).InclusiveBetween(0, 100);
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
