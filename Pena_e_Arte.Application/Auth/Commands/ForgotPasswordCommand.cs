using FluentValidation;
using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ForgotPasswordCommand(ForgotPasswordRequest Request) : IRequest<string?>;

public class ForgotPasswordHandler(IIdentityService identity)
    : IRequestHandler<ForgotPasswordCommand, string?>
{
    public async Task<string?> Handle(ForgotPasswordCommand command, CancellationToken ct)
    {
        (_, string? token, _) =
            await identity.GeneratePasswordResetTokenAsync(command.Request.Email);
        // In production: email the token. Returned here for simplicity / dev use.
        return token;
    }
}

public class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
    }
}
