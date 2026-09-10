using FluentValidation;
using Pena_e_Arte.Application.Packages.Commands;

namespace Pena_e_Arte.Application.Packages.Validators;

public class PurchasePackageValidator : AbstractValidator<PurchasePackageCommand>
{
    public PurchasePackageValidator()
    {
        RuleFor(x => x.Request.PackageId).NotEmpty();
    }
}
