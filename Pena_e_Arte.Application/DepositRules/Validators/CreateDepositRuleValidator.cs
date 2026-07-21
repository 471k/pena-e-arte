using FluentValidation;
using Pena_e_Arte.Application.DepositRules.Commands;

namespace Pena_e_Arte.Application.DepositRules.Validators;

public class CreateDepositRuleValidator : AbstractValidator<CreateDepositRuleCommand>
{
    public CreateDepositRuleValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Request.AmountFixed)
            .GreaterThan(0)
            .When(x => x.Request.AmountFixed.HasValue);

        RuleFor(x => x.Request.AmountPercent)
            .InclusiveBetween(0.01m, 100m)
            .When(x => x.Request.AmountPercent.HasValue);

        RuleFor(x => x.Request)
            .Must(r => r.AmountFixed.HasValue ^ r.AmountPercent.HasValue)
            .WithName("DepositAmount")
            .WithMessage("Exactly one of AmountFixed or AmountPercent must be specified.");

        RuleFor(x => x.Request.CancellationWindowHours)
            .GreaterThan(0)
            .When(x => x.Request.CancellationWindowHours.HasValue);

        RuleFor(x => x.Request.RefundPercentOnLateCancel)
            .InclusiveBetween(0, 100);
    }
}
