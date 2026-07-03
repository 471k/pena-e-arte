using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class OAuthLoginValidator : AbstractValidator<OAuthLoginCommand>
{
    private static readonly string[] AllowedProviders = ["google", "apple"];

    public OAuthLoginValidator()
    {
        RuleFor(x => x.Request.Provider)
            .NotEmpty()
            .Must(p => AllowedProviders.Contains(p))
            .WithMessage("Provider must be 'google' or 'apple'.");

        RuleFor(x => x.Request.IdToken)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
