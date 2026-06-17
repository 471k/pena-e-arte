using FluentValidation;
using Pena_e_Arte.Application.ConsentForms.Commands;

namespace Pena_e_Arte.Application.ConsentForms.Validators;

public class SignConsentFormValidator : AbstractValidator<SignConsentFormCommand>
{
    public SignConsentFormValidator()
    {
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.SignatureData).NotEmpty().MaximumLength(5000);
    }
}
