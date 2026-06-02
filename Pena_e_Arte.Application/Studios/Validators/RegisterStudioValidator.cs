using FluentValidation;
using Pena_e_Arte.Application.Studios.Commands;

namespace Pena_e_Arte.Application.Studios.Validators;

public class RegisterStudioValidator : AbstractValidator<RegisterStudioCommand>
{
    public RegisterStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Slug)
            .NotEmpty().MaximumLength(100)
            .Matches("^[a-z0-9-]+$")
            .WithMessage("Slug may only contain lowercase letters, numbers, and hyphens.");
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
    }
}
