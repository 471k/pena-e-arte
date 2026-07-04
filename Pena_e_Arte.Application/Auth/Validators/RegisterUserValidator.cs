using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    // This endpoint is [AllowAnonymous]. "artist" and "issuer" accounts must never be
    // self-registered here — artists are provisioned by an authenticated owner, and
    // issuer is the cross-tenant platform-admin role. Only "client" (public signup) and
    // "owner" (studio self-registration, see RegisterUserHandler's OwnerEmail check) may
    // pass through this public endpoint.
    private static readonly string[] ValidRoles = ["client", "owner"];

    public RegisterUserValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Request.Role)
            .NotEmpty()
            .Must(r => ValidRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Role must be one of: client, owner.");
        // StudioId is only required for owner self-registration (a studio must already
        // exist to be claimed). Client registration is studio-less — a client's first
        // Client row gets created on demand later, via the switch-studio flow, the first
        // time they book at a studio.
        // NotEmpty() on a nullable Guid only rejects null (its "empty" is default(Guid?),
        // not default(Guid)) — a Guid.Empty wrapped in a non-null Nullable<Guid> would
        // slip through, so check both explicitly.
        RuleFor(x => x.Request.StudioId)
            .Must(id => id is not null && id != Guid.Empty)
            .When(x => string.Equals(x.Request.Role, "owner", StringComparison.OrdinalIgnoreCase))
            .WithMessage("StudioId is required when registering as an owner.");
    }
}
