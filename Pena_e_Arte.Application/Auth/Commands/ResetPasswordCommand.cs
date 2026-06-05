using FluentValidation;
using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ResetPasswordCommand(ResetPasswordRequest Request) : IRequest;

public class ResetPasswordHandler(IIdentityService identity)
    : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        ResetPasswordRequest req = command.Request;
        (bool success, string[] errors) =
            await identity.ResetPasswordAsync(req.Email, req.Token, req.NewPassword);

        if (!success)
            throw new BusinessRuleViolationException(
                errors.Length > 0 ? string.Join(" ", errors) : "Password reset failed.");
    }
}

public class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Token).NotEmpty();
        RuleFor(x => x.Request.NewPassword).NotEmpty().MinimumLength(8);
    }
}
