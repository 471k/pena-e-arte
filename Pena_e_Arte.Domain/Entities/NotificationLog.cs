using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class NotificationLog : TenantEntity
{
    public Guid                RecipientId { get; set; }
    public NotificationChannel Channel     { get; set; }
    public string?             Subject     { get; set; }
    public string              Body        { get; set; } = string.Empty;
    public DateTime?           SentAt      { get; set; }
    public bool                IsSuccess   { get; set; }
}
