using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class CreatePaymentIntentValidator : AbstractValidator<CreatePaymentIntentCommand>
{
    public CreatePaymentIntentValidator()
    {
        RuleFor(x => x.Request.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[a-zA-Z]+$").WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}
