namespace Pena_e_Arte.Domain.Interfaces;

public interface INotificationService
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default);
    Task SendSmsAsync(string to, string body, CancellationToken ct = default);
}
