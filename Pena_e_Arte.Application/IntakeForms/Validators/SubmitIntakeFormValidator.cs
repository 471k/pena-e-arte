using FluentValidation;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.IntakeForms.Validators;

public class SubmitIntakeFormValidator : AbstractValidator<SubmitIntakeFormCommand>
{
    public SubmitIntakeFormValidator(IR2Service r2)
    {
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.FormData).NotEmpty().MaximumLength(65535);
        RuleFor(x => x.Request.FileUrl)
            .MaximumLength(1000)
            .Must(url => r2.IsR2Url(url!))
            .WithMessage("FileUrl must reference a valid storage URL.")
            .When(x => x.Request.FileUrl is not null);
    }
}
