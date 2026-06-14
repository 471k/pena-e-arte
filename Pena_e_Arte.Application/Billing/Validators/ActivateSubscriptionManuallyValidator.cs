using FluentValidation;
using Pena_e_Arte.Application.Billing.Commands;

namespace Pena_e_Arte.Application.Billing.Validators;

public class ActivateSubscriptionManuallyValidator
    : AbstractValidator<ActivateSubscriptionManuallyCommand>
{
    public ActivateSubscriptionManuallyValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}
