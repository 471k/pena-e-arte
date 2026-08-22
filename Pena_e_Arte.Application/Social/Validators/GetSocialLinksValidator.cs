using FluentValidation;
using Pena_e_Arte.Application.Social.Queries;

namespace Pena_e_Arte.Application.Social.Validators;

public class GetSocialLinksValidator : AbstractValidator<GetSocialLinksQuery>
{
    public GetSocialLinksValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.SubjectType).IsInEnum();
    }
}
