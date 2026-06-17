using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Helpers;

public sealed class AlwaysEnabledNotificationPreferences : INotificationPreferenceService
{
    public Task<bool> IsEnabledAsync(
        Guid studioId, NotificationType type, NotificationChannel channel, CancellationToken ct)
        => Task.FromResult(true);
}
