using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ResendVerificationEmailCommand(Guid UserId) : IRequest;

public class ResendVerificationEmailHandler(
    IIdentityService                        identity,
    IEmailRenderer                          emailRenderer,
    INotificationService                    notifications,
    IAppSettings                            appSettings,
    ILogger<ResendVerificationEmailHandler> logger)
    : IRequestHandler<ResendVerificationEmailCommand>
{
    public async Task Handle(ResendVerificationEmailCommand command, CancellationToken ct)
    {
        bool already = await identity.IsEmailConfirmedAsync(command.UserId, ct);
        if (already) return;

        string? email = await identity.GetUserEmailAsync(command.UserId, ct);
        if (string.IsNullOrEmpty(email)) return;

        try
        {
            string token           = await identity.GenerateEmailConfirmationTokenAsync(command.UserId);
            string confirmationUrl = $"{appSettings.BaseUrl}/verify-email?token={Uri.EscapeDataString(token)}&userId={command.UserId}";
            string body            = emailRenderer.RenderEmailVerification(confirmationUrl);

            await notifications.SendEmailAsync(
                email, "Confirm your TattooOS account", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resend verification email for user {@UserId}", command.UserId);
        }
    }
}
