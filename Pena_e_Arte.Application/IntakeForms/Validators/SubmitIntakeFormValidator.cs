using FluentValidation;
using Pena_e_Arte.Application.IntakeForms.Commands;

namespace Pena_e_Arte.Application.IntakeForms.Validators;

public class SubmitIntakeFormValidator : AbstractValidator<SubmitIntakeFormCommand>
{
    public SubmitIntakeFormValidator()
    {
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.FormData).NotEmpty().MaximumLength(65535);
        RuleFor(x => x.Request.FileUrl).MaximumLength(1000)
            .When(x => x.Request.FileUrl is not null);
    }
}
