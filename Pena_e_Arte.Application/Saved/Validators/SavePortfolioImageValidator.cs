using FluentValidation;
using Pena_e_Arte.Application.Saved.Commands;

namespace Pena_e_Arte.Application.Saved.Validators;

public class SavePortfolioImageValidator : AbstractValidator<SavePortfolioImageCommand>
{
    public SavePortfolioImageValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}

public class UnsavePortfolioImageValidator : AbstractValidator<UnsavePortfolioImageCommand>
{
    public UnsavePortfolioImageValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}
