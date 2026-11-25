using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A photo a draft points at: an area photo or reference image already uploaded to R2 by the booking form.
/// Kept as its own column, the same shape as AppointmentAttachment.ImageUrl, so StoredObjectReferenceReader
/// counts it. Otherwise the orphaned-object sweep would treat a draft's photos as unreferenced.
/// </summary>
public class BookingDraftAttachment : TenantEntity
{
    public Guid DraftId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public AppointmentAttachmentCategory Category { get; set; } = AppointmentAttachmentCategory.Reference;

    public BookingDraft Draft { get; set; } = null!;
}
