using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Pena_e_Arte.Domain.Interfaces;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Pena_e_Arte.Infrastructure.Services;

public class NotificationService(
    IConfiguration              configuration,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        MimeMessage message = new();
        message.From.Add(new MailboxAddress(
            configuration["MailKit:FromName"] ?? "Pena e Arte",
            configuration["MailKit:FromAddress"]!));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body    = new TextPart("html") { Text = body };

        using SmtpClient smtp = new();
        await smtp.ConnectAsync(
            configuration["MailKit:Host"]!,
            configuration.GetValue<int>("MailKit:Port"),
            SecureSocketOptions.Auto, ct);

        string? username = configuration["MailKit:Username"];
        if (!string.IsNullOrEmpty(username))
            await smtp.AuthenticateAsync(username, configuration["MailKit:Password"]!, ct);

        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);

        logger.LogInformation("Email sent subject {@Subject}", subject);
    }

    public async Task SendSmsAsync(string to, string body, CancellationToken ct = default)
    {
        MessageResource message = await MessageResource.CreateAsync(
            body: body,
            from: new PhoneNumber(configuration["Twilio:FromNumber"]!),
            to:   new PhoneNumber(to));

        logger.LogInformation("SMS sent SID {@MessageSid}", message.Sid);
    }
}
