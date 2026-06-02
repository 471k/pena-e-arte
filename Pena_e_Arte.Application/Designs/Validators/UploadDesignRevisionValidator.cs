using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;

namespace Pena_e_Arte.Application.Designs.Validators;

public class UploadDesignRevisionValidator : AbstractValidator<UploadDesignRevisionCommand>
{
    public UploadDesignRevisionValidator()
    {
        RuleFor(x => x.Request.DesignId).NotEmpty();
        RuleFor(x => x.Request.FileUrl).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Request.Notes).MaximumLength(2000)
            .When(x => x.Request.Notes is not null);
    }
}
