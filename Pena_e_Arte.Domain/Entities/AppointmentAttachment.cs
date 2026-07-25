namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A client-supplied reference image (style, placement, prior work) attached to an
/// appointment request. Uploaded to R2 via the shared presign flow before the
/// appointment is created — see CreateAppointmentCommand.
/// </summary>
public class AppointmentAttachment : TenantEntity
{
    public Guid   AppointmentId { get; set; }
    public string ImageUrl      { get; set; } = string.Empty;
    public DateTime UploadedAt  { get; set; } = DateTime.UtcNow;

    // Navigation
    public Appointment Appointment { get; set; } = null!;
}
