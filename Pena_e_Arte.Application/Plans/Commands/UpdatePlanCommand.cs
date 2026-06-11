using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Plans.Commands;

public record UpdatePlanCommand(Guid PlanId, UpdatePlanRequest Request) : IRequest<PlanResponse>;

public class UpdatePlanHandler(IAppDbContext db)
    : IRequestHandler<UpdatePlanCommand, PlanResponse>
{
    public async Task<PlanResponse> Handle(UpdatePlanCommand command, CancellationToken ct)
    {
        Domain.Entities.Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Plan), command.PlanId);

        UpdatePlanRequest req = command.Request;
        plan.Name                  = req.Name;
        plan.PriceMonthly          = req.PriceMonthly;
        plan.PriceYearly           = req.PriceYearly;
        plan.YearlyDiscountPercent = req.YearlyDiscountPercent;
        plan.AllowBrandingRemoval  = req.AllowBrandingRemoval;
        plan.StripePriceIdMonthly  = req.StripePriceIdMonthly;
        plan.StripePriceIdYearly   = req.StripePriceIdYearly;

        await db.SaveChangesAsync(ct);

        return new PlanResponse(
            plan.Id, plan.Name, plan.BillingInterval.ToString(),
            plan.PriceMonthly, plan.PriceYearly, plan.YearlyDiscountPercent,
            plan.AllowBrandingRemoval,
            plan.StripePriceIdMonthly, plan.StripePriceIdYearly);
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
    }
}
