using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;

namespace Pena_e_Arte.Application.Designs.Validators;

public class CreateDesignValidator : AbstractValidator<CreateDesignCommand>
{
    public CreateDesignValidator()
    {
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.ArtistId).NotEmpty();
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Description).MaximumLength(2000)
            .When(x => x.Request.Description is not null);
    }
}
