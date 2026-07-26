using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Appointment : TenantEntity
{
    public Guid ArtistId { get; set; }
    public Guid ClientId { get; set; }
    public DateTime Date { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationMinutes { get; set; }
    public AppointmentStatus Status { get; set; }
    public DepositStatus DepositStatus { get; set; }
    public decimal DepositAmount { get; set; }
    public string? Notes { get; set; }
    public CancellationReason? CancellationReason { get; set; }
    public DateTime? AftercareSentAt { get; set; }

    // Hangfire job IDs — stored so cancellation can delete the scheduled reminders
    public string? ReminderJobId48h { get; set; }
    public string? ReminderJobId24h { get; set; }

    public Artist Artist { get; set; } = null!;
    public Client Client { get; set; } = null!;

    // Reference images the client attached when requesting the appointment.
    // Empty (not null) when not eagerly loaded via .Include(a => a.Attachments).
    public ICollection<AppointmentAttachment> Attachments { get; set; } = new List<AppointmentAttachment>();
}
