using FluentValidation;
using Pena_e_Arte.Application.Billing.Commands;

namespace Pena_e_Arte.Application.Billing.Validators;

public class CreateSubscriptionValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionValidator()
    {
        RuleFor(x => x.Request.PlanId).NotEmpty();
    }
}
