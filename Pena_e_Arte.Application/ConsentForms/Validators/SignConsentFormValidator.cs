using FluentValidation;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Validators;

public class SignConsentFormValidator : AbstractValidator<SignConsentFormCommand>
{
    public SignConsentFormValidator(IR2Service r2)
    {
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.SignatureData).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.Request.FileUrl)
            .MaximumLength(1000)
            .Must(url => r2.IsR2Url(url!))
            .WithMessage("FileUrl must reference a valid storage URL.")
            .When(x => x.Request.FileUrl is not null);
    }
}
