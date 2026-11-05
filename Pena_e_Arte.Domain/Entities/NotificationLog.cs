using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class NotificationLog : TenantEntity
{
    public Guid? RecipientId { get; set; }
    public NotificationRecipientType RecipientType { get; set; }
    public NotificationChannel Channel { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public bool IsSuccess { get; set; }

    /// <summary>
    /// The appointment this message was about, when the sender records it (E04b, A08: aftercare). Null
    /// for older rows and for notifications that don't set it. Lets an appointment show whether its
    /// aftercare was sent or failed, per channel. "Sent" means our provider accepted it: nothing here
    /// records delivery or reading.
    /// </summary>
    public Guid? AppointmentId { get; set; }
}
