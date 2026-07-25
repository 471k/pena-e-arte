using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ForgotPasswordCommand(ForgotPasswordRequest Request) : IRequest;

public class ForgotPasswordHandler(
    IIdentityService                identity,
    IEmailRenderer                  emailRenderer,
    INotificationService             notifications,
    IAppSettings                     appSettings,
    ILogger<ForgotPasswordHandler>   logger)
    : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand command, CancellationToken ct)
    {
        string email = command.Request.Email;

        (bool success, string? token, _) = await identity.GeneratePasswordResetTokenAsync(email);

        // Always behave identically whether or not the account exists — this response
        // must never reveal account existence or leak the reset token to the caller.
        // The token is only ever delivered out-of-band, via email, to the account owner.
        if (!success || token is null) return;

        try
        {
            string resetUrl = $"{appSettings.BaseUrl}/reset-password" +
                               $"?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
            string body = emailRenderer.RenderPasswordReset(resetUrl);

            await notifications.SendEmailAsync(email, "Reset your TattooOS password", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send password reset email");
        }
    }
}

public class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
    }
}
