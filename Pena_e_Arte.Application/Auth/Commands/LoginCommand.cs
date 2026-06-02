using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record LoginCommand(LoginRequest Request) : IRequest<AuthResponse>;

public class LoginHandler(IIdentityService identity) : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand command, CancellationToken ct)
    {
        (bool success, string? token, string? error) =
            await identity.LoginAsync(command.Request.Email, command.Request.Password);

        if (!success) throw new BusinessRuleViolationException(error ?? "Invalid credentials.");
        return new AuthResponse(token!);
    }
}
