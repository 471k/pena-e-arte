using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record OAuthLoginCommand(OAuthLoginRequest Request) : IRequest<AuthResponse>;

public class OAuthLoginHandler(
    IOAuthTokenValidator validator,
    IIdentityService identity) : IRequestHandler<OAuthLoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(OAuthLoginCommand command, CancellationToken ct)
    {
        OAuthUserInfo info = command.Request.Provider switch
        {
            "google" => await validator.ValidateGoogleTokenAsync(command.Request.IdToken, ct),
            "apple" => await validator.ValidateAppleTokenAsync(command.Request.IdToken, ct),
            _ => throw new BusinessRuleViolationException(
                             $"Unsupported OAuth provider: {command.Request.Provider}"),
        };

        (bool success, string? accessToken, string? error) =
            await identity.LoginWithVerifiedEmailAsync(info.Email);

        if (!success)
            throw new BusinessRuleViolationException(
                error ?? "No account found. Please register first.");

        string refreshToken = await identity.CreateRefreshTokenAsync(info.Email);
        return new AuthResponse(accessToken!, refreshToken);
    }
}
