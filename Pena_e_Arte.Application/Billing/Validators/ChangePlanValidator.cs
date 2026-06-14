using FluentValidation;
using Pena_e_Arte.Application.Billing.Commands;

namespace Pena_e_Arte.Application.Billing.Validators;

public class ChangePlanValidator : AbstractValidator<ChangePlanCommand>
{
    public ChangePlanValidator()
    {
        RuleFor(x => x.Request.PlanId).NotEmpty();
    }
}
