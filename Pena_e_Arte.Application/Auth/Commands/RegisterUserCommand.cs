using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RegisterUserCommand(RegisterUserRequest Request) : IRequest;

public class RegisterUserHandler(IIdentityService identity) : IRequestHandler<RegisterUserCommand>
{
    public async Task Handle(RegisterUserCommand command, CancellationToken ct)
    {
        (bool success, string[] errors) = await identity.CreateUserAsync(
            command.Request.Email,
            command.Request.Password,
            command.Request.Role,
            command.Request.StudioId);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));
    }
}
