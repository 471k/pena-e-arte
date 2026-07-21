using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Plans.Commands;

// Plan is issuer-owned, not tenant-scoped — this is a genuinely platform-wide action
// with no single studio target, so AuditStudioId is left at its default (null).
public record UpdatePlanCommand(Guid PlanId, UpdatePlanRequest Request) : IRequest<PlanResponse>, IAuditableCommand
{
    public string AuditAction     => AuditActions.PlanUpdated;
    public string AuditTargetType => AuditTargetTypes.Plan;
    public Guid   AuditTargetId   => PlanId;
}

public class UpdatePlanHandler(IAppDbContext db)
    : IRequestHandler<UpdatePlanCommand, PlanResponse>
{
    public async Task<PlanResponse> Handle(UpdatePlanCommand command, CancellationToken ct)
    {
        Plan plan = await db.Plans
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Plan), command.PlanId);

        UpdatePlanRequest req = command.Request;

        plan.Name                     = req.Name;
        plan.YearlyDiscountPercent    = req.YearlyDiscountPercent;
        plan.AllowBrandingRemoval     = req.AllowBrandingRemoval;
        plan.MaxArtists               = req.MaxArtists;
        plan.MaxAppointmentsPerMonth  = req.MaxAppointmentsPerMonth;
        plan.MaxNotificationsPerMonth = req.MaxNotificationsPerMonth;
        plan.MaxStorageGb             = req.MaxStorageGb;
        plan.MaxLocations             = req.MaxLocations;
        plan.AllowApiAccess           = req.AllowApiAccess;
        plan.PrioritySupport          = req.PrioritySupport;

        List<PlanPrice> existingPrices = plan.Prices.ToList();
        List<BillingInterval> requestedIntervals = req.Prices
            .Select(pr => Enum.Parse<BillingInterval>(pr.Interval, ignoreCase: true))
            .ToList();

        foreach (PlanPriceRequest pr in req.Prices)
        {
            BillingInterval interval = Enum.Parse<BillingInterval>(pr.Interval, ignoreCase: true);
            PlanPrice? existing = existingPrices.FirstOrDefault(pp => pp.Interval == interval);
            if (existing is not null)
            {
                existing.Price         = pr.Price;
                existing.StripePriceId = pr.StripePriceId;
                existing.IsActive      = pr.IsActive;
            }
            else
            {
                db.PlanPrices.Add(new PlanPrice
                {
                    PlanId = plan.Id, Interval = interval, Price = pr.Price,
                    StripePriceId = pr.StripePriceId, IsActive = pr.IsActive,
                });
            }
        }

        // A price interval present on the existing plan but NOT in the request is removed —
        // this is how an issuer turns an interval off from the editor (distinct from
        // IsActive = false, which keeps the row but hides it from checkout; removing it
        // entirely means "this tier never offered this interval").
        foreach (PlanPrice stale in existingPrices.Where(ep => !requestedIntervals.Contains(ep.Interval)))
            db.PlanPrices.Remove(stale);

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
        // See CreatePlanValidator — a plan is either fully free or fully paid, never mixed.
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
