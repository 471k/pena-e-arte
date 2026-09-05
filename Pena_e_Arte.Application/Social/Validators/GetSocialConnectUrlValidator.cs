using FluentValidation;
using Pena_e_Arte.Application.Social.Queries;

namespace Pena_e_Arte.Application.Social.Validators;

public class GetSocialConnectUrlValidator : AbstractValidator<GetSocialConnectUrlQuery>
{
    public GetSocialConnectUrlValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.SubjectType).IsInEnum();
    }
}
