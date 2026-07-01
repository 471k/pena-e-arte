using MediatR;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ConfirmEmailCommand(Guid UserId, string Token) : IRequest;

public class ConfirmEmailHandler(IIdentityService identity)
    : IRequestHandler<ConfirmEmailCommand>
{
    public async Task Handle(ConfirmEmailCommand command, CancellationToken ct)
    {
        (bool success, string[] errors) =
            await identity.ConfirmEmailAsync(command.UserId, command.Token, ct);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));
    }
}
