using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class ConfirmCardPaymentValidator : AbstractValidator<ConfirmCardPaymentCommand>
{
    public ConfirmCardPaymentValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
