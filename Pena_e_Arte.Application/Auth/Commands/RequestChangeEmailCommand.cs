using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RequestChangeEmailCommand(
    Guid   UserId,
    string CurrentPassword,
    string NewEmail) : IRequest;

public class RequestChangeEmailValidator : AbstractValidator<RequestChangeEmailCommand>
{
    public RequestChangeEmailValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewEmail).NotEmpty().EmailAddress();
    }
}

public class RequestChangeEmailHandler(
    IIdentityService                    identity,
    IEmailRenderer                      emailRenderer,
    INotificationService                notifications,
    IAppSettings                        appSettings,
    ILogger<RequestChangeEmailHandler>  logger)
    : IRequestHandler<RequestChangeEmailCommand>
{
    public async Task Handle(RequestChangeEmailCommand command, CancellationToken ct)
    {
        (bool success, string? token, string[] errors, bool emailTaken) =
            await identity.GenerateChangeEmailTokenAsync(
                command.UserId, command.CurrentPassword, command.NewEmail, ct);

        if (!success)
        {
            if (emailTaken)
                throw new ConflictException(errors.Length > 0 ? errors[0] : "That email is already in use.");
            throw new BusinessRuleViolationException(
                errors.Length > 0 ? string.Join("; ", errors) : "Could not start email change.");
        }

        try
        {
            string confirmUrl = $"{appSettings.BaseUrl}/confirm-change-email" +
                $"?userId={command.UserId}&newEmail={Uri.EscapeDataString(command.NewEmail)}&token={Uri.EscapeDataString(token!)}";
            string body = emailRenderer.RenderChangeEmailConfirmation(confirmUrl);

            // Sent to the NEW address only, never the old one — this both proves the
            // requester controls that inbox and stops a hijacked session from silently
            // redirecting the account to an address the real owner never sees.
            await notifications.SendEmailAsync(
                command.NewEmail, "Confirm your new TattooOS email", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send change-email confirmation for user {@UserId}", command.UserId);
            throw new ServiceUnavailableException("Could not send the confirmation email. Please try again.");
        }
    }
}
