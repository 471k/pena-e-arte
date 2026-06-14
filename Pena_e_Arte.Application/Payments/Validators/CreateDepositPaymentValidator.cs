using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class CreateDepositPaymentValidator : AbstractValidator<CreateDepositPaymentCommand>
{
    public CreateDepositPaymentValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
