using FluentValidation;
using Pena_e_Arte.Application.Social.Commands;

namespace Pena_e_Arte.Application.Social.Validators;

public class RequestSocialVerificationCodeValidator : AbstractValidator<RequestSocialVerificationCodeCommand>
{
    public RequestSocialVerificationCodeValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.SubjectType).IsInEnum();
    }
}
