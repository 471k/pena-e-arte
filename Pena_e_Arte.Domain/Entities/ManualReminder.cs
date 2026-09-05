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

    /// <summary>
    /// Set immediately before the SMS send is attempted — durably claims this reminder so a
    /// Hangfire retry (triggered by, e.g., a transient DB failure on the post-send save, not
    /// by the send itself) can tell "already attempted, unknown outcome" apart from "never
    /// attempted" and skip re-sending rather than risk texting the client twice.
    /// </summary>
    public DateTime? SendAttemptedAt { get; set; }

    public Artist Artist { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public Client? Client { get; set; }
}
