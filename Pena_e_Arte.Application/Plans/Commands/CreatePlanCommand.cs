using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Plans.Commands;

public record CreatePlanCommand(CreatePlanRequest Request) : IRequest<PlanResponse>;

public class CreatePlanHandler(IAppDbContext db)
    : IRequestHandler<CreatePlanCommand, PlanResponse>
{
    public async Task<PlanResponse> Handle(CreatePlanCommand command, CancellationToken ct)
    {
        CreatePlanRequest req = command.Request;

        BillingInterval interval = Enum.Parse<BillingInterval>(req.BillingInterval, ignoreCase: true);

        if (req.PairedPlanId is Guid pairedId)
        {
            bool pairedExists = await db.Plans.AnyAsync(p => p.Id == pairedId, ct);
            if (!pairedExists)
                throw new NotFoundException(nameof(Plan), pairedId);
        }

        Plan plan = new()
        {
            Name                     = req.Name,
            BillingInterval          = interval,
            PriceMonthly             = req.PriceMonthly,
            PriceYearly              = req.PriceYearly,
            YearlyDiscountPercent    = req.YearlyDiscountPercent,
            StripePriceIdMonthly     = req.StripePriceIdMonthly,
            StripePriceIdYearly      = req.StripePriceIdYearly,
            MaxArtists               = req.MaxArtists,
            MaxAppointmentsPerMonth  = req.MaxAppointmentsPerMonth,
            MaxNotificationsPerMonth = req.MaxNotificationsPerMonth,
            MaxStorageGb             = req.MaxStorageGb,
            MaxLocations             = req.MaxLocations,
            AllowApiAccess           = req.AllowApiAccess,
            PrioritySupport          = req.PrioritySupport,
            PairedPlanId             = req.PairedPlanId,
        };

        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);

        return Map(plan, subscriberCount: 0);
    }

    internal static PlanResponse Map(Plan plan, int subscriberCount) => new(
        plan.Id, plan.Name, plan.BillingInterval.ToString(),
        plan.PriceMonthly, plan.PriceYearly, plan.YearlyDiscountPercent,
        plan.AllowBrandingRemoval,
        plan.StripePriceIdMonthly, plan.StripePriceIdYearly,
        subscriberCount,
        plan.MaxArtists, plan.MaxAppointmentsPerMonth, plan.MaxNotificationsPerMonth,
        plan.MaxStorageGb, plan.MaxLocations, plan.AllowApiAccess, plan.PrioritySupport,
        plan.PairedPlanId);
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
