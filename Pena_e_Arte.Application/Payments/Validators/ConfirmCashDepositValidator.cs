using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class ConfirmCashDepositValidator : AbstractValidator<ConfirmCashDepositCommand>
{
    public ConfirmCashDepositValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
