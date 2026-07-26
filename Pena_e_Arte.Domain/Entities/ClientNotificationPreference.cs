using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Records a client's opt-in/opt-out preference for a specific notification
/// type and channel from a specific studio.
/// No global query filter — scoped by (UserId, StudioId) in every query, since a
/// client may hold preferences for studios that are not their active JWT tenant.
/// </summary>
public class ClientNotificationPreference
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid StudioId { get; set; }
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public bool IsEnabled { get; set; } = true;
}
