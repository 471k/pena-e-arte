using FluentValidation;
using Pena_e_Arte.Application.Services.Commands;

namespace Pena_e_Arte.Application.Services.Validators;

public class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Description).MaximumLength(2000);

        RuleFor(x => x.Request.DurationMinutes).InclusiveBetween(5, 600);

        RuleFor(x => x.Request.Price)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.Price.HasValue);

        RuleFor(x => x.Request.DepositAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.DepositAmount.HasValue);
    }
}
