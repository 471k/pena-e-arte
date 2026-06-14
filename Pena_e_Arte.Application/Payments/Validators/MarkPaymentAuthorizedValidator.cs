using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class MarkPaymentAuthorizedValidator : AbstractValidator<MarkPaymentAuthorizedCommand>
{
    public MarkPaymentAuthorizedValidator()
    {
        RuleFor(x => x.StripePaymentIntentId).NotEmpty();
    }
}
