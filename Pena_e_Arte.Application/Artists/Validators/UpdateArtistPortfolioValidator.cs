using FluentValidation;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Constants;

namespace Pena_e_Arte.Application.Artists.Validators;

public class UpdateArtistPortfolioValidator : AbstractValidator<UpdateArtistPortfolioCommand>
{
    public UpdateArtistPortfolioValidator()
    {
        RuleFor(x => x.Request.Images).NotNull();
        RuleForEach(x => x.Request.Images).ChildRules(image =>
        {
            image.RuleFor(i => i.ImageUrl)
                .NotEmpty()
                .MaximumLength(2048)
                .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
                .WithMessage("Each image must be a valid absolute URL.");
            image.RuleFor(i => i.Style)
                .Must(s => s is null || TattooStyle.All.Contains(s))
                .WithMessage($"Style must be one of: {string.Join(", ", TattooStyle.All)}.");
        });
        RuleFor(x => x.Request.Images.Count).LessThanOrEqualTo(50)
            .WithMessage("A maximum of 50 portfolio images are allowed.");
    }
}
