using FluentValidation;
using Pena_e_Arte.Application.Packages.Commands;

namespace Pena_e_Arte.Application.Packages.Validators;

public class CreatePackageValidator : AbstractValidator<CreatePackageCommand>
{
    public CreatePackageValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.SessionCount).GreaterThan(0);
        RuleFor(x => x.Request.Price).GreaterThan(0);
    }
}
