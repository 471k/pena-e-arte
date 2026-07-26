using FluentValidation;
using MediatR;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : IRequest;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Must contain at least one digit.");
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must differ from current password.");
    }
}

public class ChangePasswordHandler(IIdentityService identity)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        (bool success, string[] errors) =
            await identity.ChangePasswordAsync(command.UserId, command.CurrentPassword, command.NewPassword, ct);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));
    }
}
