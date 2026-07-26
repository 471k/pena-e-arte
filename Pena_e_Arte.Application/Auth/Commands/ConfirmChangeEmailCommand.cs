using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record ConfirmChangeEmailCommand(Guid UserId, string NewEmail, string Token) : IRequest;

public class ConfirmChangeEmailHandler(
    IIdentityService identity,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    ILogger<ConfirmChangeEmailHandler> logger)
    : IRequestHandler<ConfirmChangeEmailCommand>
{
    public async Task Handle(ConfirmChangeEmailCommand command, CancellationToken ct)
    {
        // Captured before the switch — GetUserEmailAsync would otherwise return the new
        // address, and the security notice below must go to the address being replaced.
        string? oldEmail = await identity.GetUserEmailAsync(command.UserId, ct);

        (bool success, string[] errors, bool tokenInvalid, bool emailTaken) =
            await identity.ConfirmChangeEmailAsync(command.UserId, command.NewEmail, command.Token, ct);

        if (!success)
        {
            if (tokenInvalid) throw new ChangeEmailTokenInvalidException();
            if (emailTaken) throw new ConflictException(errors.Length > 0 ? errors[0] : "That email is already in use.");
            throw new BusinessRuleViolationException(
                errors.Length > 0 ? string.Join("; ", errors) : "Could not change email.");
        }

        if (!string.IsNullOrEmpty(oldEmail))
        {
            try
            {
                string body = emailRenderer.RenderEmailChangedNotice(command.NewEmail);
                await notifications.SendEmailAsync(
                    oldEmail, "Your TattooOS email address was changed", body, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send email-changed notice for user {@UserId}", command.UserId);
            }
        }
    }
}
