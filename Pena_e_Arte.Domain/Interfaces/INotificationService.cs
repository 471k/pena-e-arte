namespace Pena_e_Arte.Domain.Interfaces;

public interface INotificationService
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default);

    /// <summary>Send an email with a Reply-To address (e.g. a contact-form submitter's email so a
    /// reply goes back to them, not to the platform From address).</summary>
    Task SendEmailAsync(string to, string subject, string body, string? replyTo, CancellationToken ct = default);

    Task SendSmsAsync(string to, string body, CancellationToken ct = default);
}
