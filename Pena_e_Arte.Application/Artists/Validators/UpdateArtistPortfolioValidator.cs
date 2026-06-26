using FluentValidation;
using Pena_e_Arte.Application.Artists.Commands;

namespace Pena_e_Arte.Application.Artists.Validators;

public class UpdateArtistPortfolioValidator : AbstractValidator<UpdateArtistPortfolioCommand>
{
    public UpdateArtistPortfolioValidator()
    {
        RuleFor(x => x.Request.ImageUrls).NotNull();
        RuleForEach(x => x.Request.ImageUrls)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("Each image must be a valid absolute URL.");
        RuleFor(x => x.Request.ImageUrls.Count).LessThanOrEqualTo(50)
            .WithMessage("A maximum of 50 portfolio images are allowed.");
    }
}
