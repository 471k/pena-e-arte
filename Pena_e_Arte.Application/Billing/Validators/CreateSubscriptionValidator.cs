using FluentValidation;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing.Validators;

public class CreateSubscriptionValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionValidator()
    {
        RuleFor(x => x.Request.PlanId).NotEmpty();
        RuleFor(x => x.Request.BillingInterval)
            .NotEmpty()
            .Must(v => Enum.TryParse<BillingInterval>(v, ignoreCase: true, out _))
            .WithMessage("BillingInterval must be 'Monthly' or 'Yearly'.");
    }
}
