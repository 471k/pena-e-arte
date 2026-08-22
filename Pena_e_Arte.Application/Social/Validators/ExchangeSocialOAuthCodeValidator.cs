using FluentValidation;
using Pena_e_Arte.Application.Social.Commands;

namespace Pena_e_Arte.Application.Social.Validators;

public class ExchangeSocialOAuthCodeValidator : AbstractValidator<ExchangeSocialOAuthCodeCommand>
{
    public ExchangeSocialOAuthCodeValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.SubjectType).IsInEnum();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(512);
    }
}
