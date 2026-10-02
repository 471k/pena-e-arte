using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Resend;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Pena_e_Arte.Infrastructure.Services;

public class NotificationService(
    IConfiguration configuration,
    IResend resend,
    ILogger<NotificationService> logger) : INotificationService
{
    public Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default) =>
        SendEmailAsync(to, subject, body, replyTo: null, ct);

    public async Task SendEmailAsync(
        string to, string subject, string body, string? replyTo, CancellationToken ct = default)
    {
        EmailMessage message = new()
        {
            From = $"{configuration["Resend:FromName"] ?? "TattooOS"} <{configuration["Resend:FromAddress"]}>",
            Subject = subject,
            HtmlBody = body,
        };
        message.To.Add(to);
        if (!string.IsNullOrWhiteSpace(replyTo))
            message.ReplyTo = replyTo;

        ResendResponse<Guid> response = await resend.EmailSendAsync(message, ct);

        logger.LogInformation("Email sent subject {@Subject} id {@EmailId}", subject, response.Content);
    }

    public async Task SendSmsAsync(string to, string body, CancellationToken ct = default)
    {
        // The single choke point every SMS in the app goes through (reminders, confirmations, payment
        // notices), so one check covers them all. See SmsRecipientPolicy.
        if (!SmsRecipientPolicy.IsAllowed(configuration, to))
        {
            logger.LogWarning("SMS blocked: recipient is not on this environment's allow-list");
            throw new SmsRecipientNotAllowedException();
        }

        MessageResource message = await MessageResource.CreateAsync(
            body: body,
            from: new PhoneNumber(configuration["Twilio:FromNumber"]!),
            to: new PhoneNumber(to));

        logger.LogInformation("SMS sent SID {@MessageSid}", message.Sid);
    }
}
