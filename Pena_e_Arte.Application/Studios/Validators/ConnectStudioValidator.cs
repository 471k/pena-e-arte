using FluentValidation;
using Pena_e_Arte.Application.Studios.Commands;

namespace Pena_e_Arte.Application.Studios.Validators;

public class ConnectStudioValidator : AbstractValidator<ConnectStudioCommand>
{
    public ConnectStudioValidator()
    {
        RuleFor(x => x.Request.ReturnUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ReturnUrl must be a valid absolute URL.");

        RuleFor(x => x.Request.RefreshUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("RefreshUrl must be a valid absolute URL.");

        RuleFor(x => x.Request.Country)
            .NotEmpty()
            .Length(2)
            .Matches("^[a-zA-Z]+$")
            .WithMessage("Country must be a 2-letter ISO 3166-1 alpha-2 country code.");
    }
}
