using System.Net;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Contact.Commands;

/// <summary>
/// Public contact-form submission (anonymous). Relays the message to the support inbox by email
/// with the submitter set as Reply-To. Deliberately NOT persisted — send-only avoids creating a new
/// PII-retention surface (no ContactRequest table); the email delivery is the whole record.
/// </summary>
public record SubmitContactRequestCommand(SubmitContactRequest Request) : IRequest<Unit>;

public class SubmitContactRequestHandler(
    INotificationService notifications,
    ILogger<SubmitContactRequestHandler> logger)
    : IRequestHandler<SubmitContactRequestCommand, Unit>
{
    // Founder-confirmed public support inbox (2026-08-01). Not a secret.
    private const string SupportEmail = "support@tattooos.co";

    public async Task<Unit> Handle(SubmitContactRequestCommand command, CancellationToken ct)
    {
        SubmitContactRequest req = command.Request;

        // HTML-encode the user-supplied fields — this body is sent as HtmlBody.
        string safeName = WebUtility.HtmlEncode(req.Name);
        string safeEmail = WebUtility.HtmlEncode(req.Email);
        string safeMessage = WebUtility.HtmlEncode(req.Message).Replace("\n", "<br/>");

        string body =
            $"<p><strong>New contact-form message</strong></p>" +
            $"<p><strong>From:</strong> {safeName} &lt;{safeEmail}&gt;</p>" +
            $"<hr/><p>{safeMessage}</p>";

        // Reply-To = submitter so support can reply straight back to them.
        await notifications.SendEmailAsync(
            to: SupportEmail,
            subject: $"Contact form: {req.Name}",
            body: body,
            replyTo: req.Email,
            ct: ct);

        // Never log the name/email/message (PII — rule 3); log only that one was relayed.
        logger.LogInformation("Contact-form message relayed to support inbox.");
        return Unit.Value;
    }
}

public class SubmitContactRequestValidator : AbstractValidator<SubmitContactRequestCommand>
{
    public SubmitContactRequestValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Request.Message).NotEmpty().MaximumLength(2000);
    }
}
