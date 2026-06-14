using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class DeclareCashDepositValidator : AbstractValidator<DeclareCashDepositCommand>
{
    public DeclareCashDepositValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}
