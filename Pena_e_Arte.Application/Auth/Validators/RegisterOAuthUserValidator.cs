using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class RegisterOAuthUserValidator : AbstractValidator<RegisterOAuthUserCommand>
{
    private static readonly string[] AllowedProviders = ["google", "apple"];

    // Same restriction as RegisterUserValidator: this endpoint is [AllowAnonymous], so
    // "artist" and "admin" accounts must never be self-registered here — artists are
    // provisioned by an authenticated owner, and admin is the cross-tenant platform-admin
    // role. Only "client" (public signup) and "owner" (studio self-registration, see
    // RegisterOAuthUserHandler's OwnerEmail check) may pass through this public endpoint.
    private static readonly string[] AllowedRoles = ["client", "owner"];

    public RegisterOAuthUserValidator()
    {
        RuleFor(x => x.Request.Provider)
            .NotEmpty()
            .Must(p => AllowedProviders.Contains(p))
            .WithMessage("Provider must be 'google' or 'apple'.");

        RuleFor(x => x.Request.IdToken)
            .NotEmpty()
            .MaximumLength(4096);

        RuleFor(x => x.Request.Role)
            .NotEmpty()
            .Must(r => AllowedRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Role must be 'owner' or 'client'.");

        RuleFor(x => x.Request.StudioId)
            .NotEmpty();
    }
}
