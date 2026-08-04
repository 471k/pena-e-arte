using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class MarkPaymentFailedValidator : AbstractValidator<MarkPaymentFailedCommand>
{
    public MarkPaymentFailedValidator()
    {
        RuleFor(x => x.ProviderReferenceId).NotEmpty();
    }
}
