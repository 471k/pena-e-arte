using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty();
    }
}
