using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

public interface INotificationPreferenceService
{
    Task<bool> IsEnabledAsync(Guid studioId, NotificationType type, NotificationChannel channel, CancellationToken ct);
}
