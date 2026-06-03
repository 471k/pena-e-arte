using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Validators;

public class UploadDesignRevisionValidator : AbstractValidator<UploadDesignRevisionCommand>
{
    public UploadDesignRevisionValidator(IR2Service r2)
    {
        RuleFor(x => x.Request.DesignId).NotEmpty();
        RuleFor(x => x.Request.FileUrl)
            .NotEmpty()
            .MaximumLength(1000)
            .Must(r2.IsR2Url)
            .WithMessage("FileUrl must reference a valid storage URL.");
        RuleFor(x => x.Request.Notes).MaximumLength(2000)
            .When(x => x.Request.Notes is not null);
    }
}
