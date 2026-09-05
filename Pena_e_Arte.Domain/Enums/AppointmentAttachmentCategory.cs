namespace Pena_e_Arte.Domain.Enums;

public enum AppointmentAttachmentCategory
{
    /// <summary>A straight-on photo of the body area to be tattooed.</summary>
    AreaPhoto,

    /// <summary>Style/placement/prior-work inspiration images. The only category that
    /// existed before this change — pre-existing rows backfill to this value (migration).</summary>
    Reference
}
