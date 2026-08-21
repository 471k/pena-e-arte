using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class ManualReminder : TenantEntity
{
    public Guid ArtistId { get; set; }              // who set the reminder
    public Guid? AppointmentId { get; set; }         // set when tied to an existing appointment
    public Guid? ClientId { get; set; }              // set when tied to an existing Client record
    public string RecipientName { get; set; } = string.Empty;   // always populated
    public string RecipientPhone { get; set; } = string.Empty;  // always populated
    public string? Message { get; set; }             // null = use the default template
    public DateTime ScheduledFor { get; set; }        // UTC; "now" for an immediate send
    public ManualReminderStatus Status { get; set; } = ManualReminderStatus.Scheduled;
    public string? JobId { get; set; }                // Hangfire job id, for cancellation
    public DateTime? SentAt { get; set; }

    public Artist Artist { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public Client? Client { get; set; }
}
