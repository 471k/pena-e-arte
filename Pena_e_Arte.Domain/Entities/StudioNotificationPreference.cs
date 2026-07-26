using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class StudioNotificationPreference : TenantEntity
{
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public bool IsEnabled { get; set; } = true;
}
