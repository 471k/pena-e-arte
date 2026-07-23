using System.Text.RegularExpressions;
using FluentValidation;
using Pena_e_Arte.Application.Studios.Commands;

namespace Pena_e_Arte.Application.Studios.Validators;

public class RegisterStudioValidator : AbstractValidator<RegisterStudioCommand>
{
    private static readonly Regex NiptFormat = new(@"^[A-Z]\d{8}[A-Z]$", RegexOptions.Compiled);

    public RegisterStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Slug)
            .NotEmpty().MaximumLength(100)
            .Matches("^[a-z0-9-]+$")
            .WithMessage("Slug may only contain lowercase letters, numbers, and hyphens.");
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.OwnerEmail).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Request.Nipt)
            .NotEmpty()
            .Length(10)
            .Must(n => NiptFormat.IsMatch(n.Trim().ToUpperInvariant()))
            .WithMessage("NIPT must be 10 characters: a letter, 8 digits, then a letter (e.g. L01234567A).");
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
    }
}
