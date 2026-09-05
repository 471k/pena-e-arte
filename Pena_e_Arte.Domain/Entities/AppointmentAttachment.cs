using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A client-supplied reference image (style, placement, prior work) attached to an
/// appointment request. Uploaded to R2 via the shared presign flow before the
/// appointment is created — see CreateAppointmentCommand.
/// </summary>
public class AppointmentAttachment : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Area photo (of the body) vs. reference/inspiration image. Defaults to
    /// Reference — the only category that existed before this distinction, so pre-existing
    /// rows and any code path that doesn't set it explicitly backfill correctly.</summary>
    public AppointmentAttachmentCategory Category { get; set; } = AppointmentAttachmentCategory.Reference;

    // Navigation
    public Appointment Appointment { get; set; } = null!;
}
