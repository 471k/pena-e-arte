using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;

namespace Pena_e_Arte.Application.Designs.Validators;

public class ReviewDesignValidator : AbstractValidator<ReviewDesignCommand>
{
    public ReviewDesignValidator()
    {
        RuleFor(x => x.Request.DesignRevisionId).NotEmpty();
        RuleFor(x => x.Request.Notes).MaximumLength(2000)
            .When(x => x.Request.Notes is not null);
    }
}
