using FluentValidation;
using Pena_e_Arte.Application.Social.Commands;

namespace Pena_e_Arte.Application.Social.Validators;

public class DisconnectSocialAccountValidator : AbstractValidator<DisconnectSocialAccountCommand>
{
    public DisconnectSocialAccountValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.SubjectType).IsInEnum();
    }
}
