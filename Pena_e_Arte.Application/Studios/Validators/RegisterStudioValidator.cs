using System.Text.RegularExpressions;
using FluentValidation;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Domain.Money;

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
        RuleFor(x => x.Request.AddressLine1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Request.AddressLine2).MaximumLength(150);
        RuleFor(x => x.Request.PostalCode).MaximumLength(20);
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);

        RuleFor(x => x.Request.CountryCode)
            .NotEmpty().Length(2)
            .Must(CountryCurrency.IsKnownCountry)
            .WithMessage("Country must be a known ISO 3166-1 alpha-2 code.");
        RuleFor(x => x.Request.Currency)
            .Must(c => CurrencyCatalog.IsSupported(c!))
            .WithMessage("Currency must be a known ISO 4217 code.")
            .When(x => x.Request.Currency is not null);
        RuleFor(x => x.Request)
            .Must(r => r.Currency is not null || CountryCurrency.DefaultCurrencyFor(r.CountryCode ?? "") is not null)
            .WithMessage("Please choose your studio's currency.")
            .WithName("Currency")
            .When(x => x.Request.Currency is null && x.Request.CountryCode is not null);
    }
}
