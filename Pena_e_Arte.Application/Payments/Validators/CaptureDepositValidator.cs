using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class CaptureDepositValidator : AbstractValidator<CaptureDepositCommand>
{
    public CaptureDepositValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
