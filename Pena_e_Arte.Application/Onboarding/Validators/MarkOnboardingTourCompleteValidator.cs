using FluentValidation;
using Pena_e_Arte.Application.Onboarding.Commands;

namespace Pena_e_Arte.Application.Onboarding.Validators;

public class MarkOnboardingTourCompleteValidator : AbstractValidator<MarkOnboardingTourCompleteCommand>
{
    private static readonly string[] ValidRoles = ["client", "artist", "owner", "issuer"];

    public MarkOnboardingTourCompleteValidator()
    {
        RuleFor(x => x.Request.Role)
            .NotEmpty()
            .Must(role => ValidRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Role must be one of: client, artist, owner, issuer.");
    }
}
