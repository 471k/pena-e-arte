using FluentValidation;
using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Plans.Commands;

public record CreatePlanCommand(CreatePlanRequest Request) : IRequest<PlanResponse>;

public class CreatePlanHandler(IAppDbContext db)
    : IRequestHandler<CreatePlanCommand, PlanResponse>
{
    public async Task<PlanResponse> Handle(CreatePlanCommand command, CancellationToken ct)
    {
        CreatePlanRequest req = command.Request;

        Plan plan = new()
        {
            Name                     = req.Name,
            YearlyDiscountPercent    = req.YearlyDiscountPercent,
            AllowBrandingRemoval     = req.AllowBrandingRemoval,
            MaxArtists               = req.MaxArtists,
            MaxAppointmentsPerMonth  = req.MaxAppointmentsPerMonth,
            MaxNotificationsPerMonth = req.MaxNotificationsPerMonth,
            MaxStorageGb             = req.MaxStorageGb,
            MaxLocations             = req.MaxLocations,
            AllowApiAccess           = req.AllowApiAccess,
            PrioritySupport          = req.PrioritySupport,
        };

        foreach (PlanPriceRequest pr in req.Prices)
        {
            plan.Prices.Add(new PlanPrice
            {
                Interval      = Enum.Parse<BillingInterval>(pr.Interval, ignoreCase: true),
                Price         = pr.Price,
                StripePriceId = pr.StripePriceId,
                IsActive      = pr.IsActive,
            });
        }

        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);

        return Map(plan, subscriberCount: 0);
    }

    internal static PlanResponse Map(Plan plan, int subscriberCount) => new(
        plan.Id, plan.Name, plan.YearlyDiscountPercent, plan.AllowBrandingRemoval,
        subscriberCount,
        plan.MaxArtists, plan.MaxAppointmentsPerMonth, plan.MaxNotificationsPerMonth,
        plan.MaxStorageGb, plan.MaxLocations, plan.AllowApiAccess, plan.PrioritySupport,
        plan.Prices.Select(pp => new PlanPriceResponse(
            pp.Id, pp.Interval.ToString(), pp.Price, pp.StripePriceId, pp.IsActive)).ToList());
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
