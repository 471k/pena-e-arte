using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    private static readonly string[] ValidRoles = ["client", "artist", "owner", "issuer"];

    public RegisterUserValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Request.Role)
            .NotEmpty()
            .Must(r => ValidRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Role must be one of: client, artist, owner, issuer.");
        RuleFor(x => x.Request.StudioId).NotEmpty();
    }
}
