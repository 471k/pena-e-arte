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

        BillingInterval interval = Enum.Parse<BillingInterval>(req.BillingInterval, ignoreCase: true);

        Plan plan = new()
        {
            Name                  = req.Name,
            BillingInterval       = interval,
            PriceMonthly          = req.PriceMonthly,
            PriceYearly           = req.PriceYearly,
            YearlyDiscountPercent = req.YearlyDiscountPercent,
            StripePriceIdMonthly  = req.StripePriceIdMonthly,
            StripePriceIdYearly   = req.StripePriceIdYearly,
        };

        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);

        return new PlanResponse(
            plan.Id, plan.Name, plan.BillingInterval.ToString(),
            plan.PriceMonthly, plan.PriceYearly, plan.YearlyDiscountPercent,
            plan.StripePriceIdMonthly, plan.StripePriceIdYearly);
    }
}

public class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.BillingInterval)
            .NotEmpty()
            .Must(v => Enum.TryParse<BillingInterval>(v, ignoreCase: true, out _))
            .WithMessage("BillingInterval must be 'Monthly' or 'Yearly'.");
        RuleFor(x => x.Request.PriceMonthly).GreaterThan(0);
        RuleFor(x => x.Request.PriceYearly).GreaterThan(0);
        RuleFor(x => x.Request.YearlyDiscountPercent).InclusiveBetween(0, 100);
    }
}
