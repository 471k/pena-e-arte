using FluentValidation;
using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RefreshTokenCommand(RefreshTokenRequest Request) : IRequest<AuthResponse>;

public class RefreshTokenHandler(IIdentityService identity) : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        (bool success, string? accessToken, string? refreshToken, string? error) =
            await identity.RefreshTokenAsync(command.Request.RefreshToken);

        if (!success) throw new BusinessRuleViolationException(error ?? "Invalid refresh token.");
        return new AuthResponse(accessToken!, refreshToken!);
    }
}

public class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.Request.RefreshToken).NotEmpty();
    }
}
