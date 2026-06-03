using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class UpdateSessionSplitsValidator : AbstractValidator<UpdateSessionSplitsCommand>
{
    public UpdateSessionSplitsValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Request.Splits).NotEmpty().WithMessage("At least one session split is required.");
        RuleForEach(x => x.Request.Splits).ChildRules(split =>
        {
            split.RuleFor(s => s.Label).NotEmpty().MaximumLength(100);
            split.RuleFor(s => s.Amount).GreaterThan(0);
        });
    }
}
